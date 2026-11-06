using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Artemis.Core;
using Artemis.Core.Modules;
using Artemis.Plugins.Modules.LinuxLockKeys.DataModels;

namespace Artemis.Plugins.Modules.LinuxLockKeys;

// The compositor owns lock state on Linux and mirrors it onto every keyboard's LEDs, so the LEDs
// are the source of truth. Key presses can't be used: counting them drifts out of sync.
// One thread sleeps in poll() on the keyboards' event nodes (an EV_LED event arrives whenever the
// compositor changes an LED), an inotify watch on /dev/input (wireless keyboards get a new node on
// every reconnect) and an eventfd that Disable() uses to wake it.
[PluginFeature(Name = "Linux Lock Keys", AlwaysEnabled = true)]
public class LockKeysModule : Module<LockKeysDataModel>
{
    private const string InputClass = "/sys/class/input";
    private const string DevInput = "/dev/input";

    private const int O_RDONLY_NONBLOCK_CLOEXEC = 0x800 | 0x80000;
    private const int IN_NONBLOCK_CLOEXEC = 0x800 | 0x80000;
    private const uint IN_CREATE_ATTRIB = 0x100 | 0x4;
    private const short POLLIN = 0x1;
    private const short POLL_GONE = 0x8 | 0x10 | 0x20; // POLLERR | POLLHUP | POLLNVAL
    private const ulong EVIOCGLED = 0x80084519; // _IOR('E', 0x19, 8 bytes)

    private readonly Dictionary<int, string> _keyboards = new();
    private Thread _thread;
    private int _wake = -1;
    private int _inotify = -1;
    private volatile bool _running;

    public override List<IModuleActivationRequirement> ActivationRequirements => null;

    public override void Enable()
    {
        _wake = eventfd(0, 0x80000);
        _inotify = inotify_init1(IN_NONBLOCK_CLOEXEC);
        if (_inotify >= 0)
            inotify_add_watch(_inotify, DevInput, IN_CREATE_ATTRIB);

        _running = true;
        _thread = new Thread(Run) {IsBackground = true, Name = "Linux Lock Keys"};
        _thread.Start();
    }

    public override void Disable()
    {
        _running = false;
        ulong one = 1;
        write(_wake, ref one, 8);
        _thread?.Join(TimeSpan.FromSeconds(2));

        foreach (int fd in _keyboards.Keys)
            close(fd);
        _keyboards.Clear();
        close(_inotify);
        close(_wake);
        _inotify = _wake = -1;
    }

    public override void Update(double deltaTime)
    {
    }

    private void Run()
    {
        OpenKeyboards();
        while (_running)
        {
            ReadState();

            PollFd[] fds = new[] {_wake, _inotify}.Where(fd => fd >= 0).Concat(_keyboards.Keys)
                .Select(fd => new PollFd {Fd = fd, Events = POLLIN}).ToArray();
            if (poll(fds, (ulong) fds.Length, -1) < 0)
                continue;

            bool rescan = false;
            foreach (PollFd p in fds.Where(p => p.Revents != 0))
            {
                if (p.Fd == _wake)
                    return;
                if (p.Fd == _inotify)
                    rescan = true;
                if ((p.Revents & POLL_GONE) != 0)
                {
                    close(p.Fd);
                    _keyboards.Remove(p.Fd);
                }
                else
                {
                    Drain(p.Fd);
                }
            }

            if (rescan)
                OpenKeyboards();
        }
    }

    private void OpenKeyboards()
    {
        if (!Directory.Exists(InputClass))
            return;

        foreach (string entry in Directory.EnumerateFileSystemEntries(InputClass, "event*"))
        {
            string node = Path.Combine(DevInput, Path.GetFileName(entry));
            if (_keyboards.ContainsValue(node) || !IsKeyboard(entry))
                continue;

            // Fails with EACCES until udev has applied the input group, which raises IN_ATTRIB and retries
            int fd = open(node, O_RDONLY_NONBLOCK_CLOEXEC);
            if (fd >= 0)
                _keyboards[fd] = node;
        }
    }

    // Any node with lock LEDs will do, since the compositor keeps them all in step. Nodes that also
    // report pointer motion (some gaming mice advertise LEDs) are skipped, or every mouse move would wake the thread.
    private static bool IsKeyboard(string entry)
    {
        return (Capabilities(entry, "led") & 0b111) != 0 && (Capabilities(entry, "rel") & 0b11) == 0;
    }

    // capabilities/* are space-separated hex words, most significant first; only the lowest bits are needed
    private static ulong Capabilities(string entry, string type)
    {
        try
        {
            string text = File.ReadAllText(Path.Combine(entry, "device/capabilities", type)).Trim();
            return Convert.ToUInt64(text.Split(' ').Last(), 16);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private void ReadState()
    {
        byte[] leds = new byte[8];
        foreach (int fd in _keyboards.Keys)
        {
            if (ioctl(fd, EVIOCGLED, leds) < 0)
                continue;

            DataModel.NumLock = (leds[0] & 1) != 0;
            DataModel.CapsLock = (leds[0] & 2) != 0;
            DataModel.ScrollLock = (leds[0] & 4) != 0;
            return;
        }
    }

    // Key presses arrive on the same node; they only matter as a wake-up
    private static void Drain(int fd)
    {
        byte[] buffer = new byte[4096];
        while (read(fd, buffer, (nint) buffer.Length) > 0)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc")]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, byte[] buffer, nint count);

    [DllImport("libc")]
    private static extern nint write(int fd, ref ulong value, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, byte[] buffer);

    [DllImport("libc", SetLastError = true)]
    private static extern int poll([In, Out] PollFd[] fds, ulong count, int timeout);

    [DllImport("libc")]
    private static extern int eventfd(uint initial, int flags);

    [DllImport("libc")]
    private static extern int inotify_init1(int flags);

    [DllImport("libc")]
    private static extern int inotify_add_watch(int fd, string path, uint mask);
}
