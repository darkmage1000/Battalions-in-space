using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace BIS.Net
{
    public enum Msg : byte
    {
        Hello = 1, Lobby, Start, Ready, Bye,
        Input = 10, SpawnReq, EnterReq, ExitReq, SeatReq, HeroReq,
        Snapshot = 30, LocalState, Stats,
        UnitSpawn = 40, UnitDie, VehSpawn, VehGone, Shot, Fx, Kill, Text, Hit, Hurt, Points, PlayerDied, Say, End, Burst,
        Warp = 60, Push, SelfDmg, Award, Evade
    }

    /// <summary>
    /// One TCP connection carrying length-prefixed messages (int length, then payload; the first payload byte is
    /// the Msg type). A reader thread queues incoming messages for the main thread; a writer thread sends.
    /// </summary>
    public class NetLink
    {
        public const int Port = 47777, DiscoveryPort = 47778;
        public const string Version = "BIS-1";
        readonly TcpClient tcp;
        readonly NetworkStream stream;
        readonly ConcurrentQueue<byte[]> inbox = new ConcurrentQueue<byte[]>();
        readonly BlockingCollection<byte[]> outbox = new BlockingCollection<byte[]>();
        volatile bool closed;
        public bool Closed => closed;
        public string Remote;
        public long bytesIn, bytesOut;

        public NetLink(TcpClient c)
        {
            tcp = c; tcp.NoDelay = true;
            stream = tcp.GetStream();
            try { Remote = ((IPEndPoint)tcp.Client.RemoteEndPoint).Address.ToString(); } catch { Remote = "?"; }
            new Thread(ReadLoop) { IsBackground = true, Name = "net-read" }.Start();
            new Thread(WriteLoop) { IsBackground = true, Name = "net-write" }.Start();
        }

        public static NetLink Connect(string host, int timeoutMs = 3000)
        {
            var c = new TcpClient();
            var ar = c.BeginConnect(host, Port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) { c.Close(); return null; }
            try { c.EndConnect(ar); } catch { c.Close(); return null; }
            return new NetLink(c);
        }

        void ReadLoop()
        {
            var len = new byte[4];
            try
            {
                while (!closed)
                {
                    if (!ReadExact(len, 4)) break;
                    int n = BitConverter.ToInt32(len, 0);
                    if (n <= 0 || n > 4 * 1024 * 1024) break;
                    var buf = new byte[n];
                    if (!ReadExact(buf, n)) break;
                    Interlocked.Add(ref bytesIn, n + 4);
                    inbox.Enqueue(buf);
                }
            }
            catch { }
            Close();
        }

        bool ReadExact(byte[] b, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = stream.Read(b, got, n - got);
                if (r <= 0) return false;
                got += r;
            }
            return true;
        }

        void WriteLoop()
        {
            try
            {
                foreach (var m in outbox.GetConsumingEnumerable())
                {
                    if (closed) break;
                    stream.Write(BitConverter.GetBytes(m.Length), 0, 4);
                    stream.Write(m, 0, m.Length);
                    Interlocked.Add(ref bytesOut, m.Length + 4);
                }
            }
            catch { }
            Close();
        }

        public void Send(byte[] payload) { if (!closed) outbox.Add(payload); }
        public void Send(W w) => Send(w.Done());

        public bool TryRead(out byte[] m) => inbox.TryDequeue(out m);

        public void Close()
        {
            if (closed) return;
            closed = true;
            try { outbox.CompleteAdding(); } catch { }
            try { tcp.Close(); } catch { }
        }

        /// <summary>This device's address on the local network (no packets are sent).</summary>
        public static string LocalIp()
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    s.Connect("10.255.255.255", 65530);
                    return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
                }
            }
            catch { return "?"; }
        }
    }

    /// <summary>Message writer (BinaryWriter over a MemoryStream) with helpers for game types.</summary>
    public class W
    {
        readonly MemoryStream ms = new MemoryStream(256);
        readonly BinaryWriter w;
        public W(Msg type) { w = new BinaryWriter(ms); w.Write((byte)type); }
        public W B(bool v) { w.Write(v); return this; }
        public W U8(int v) { w.Write((byte)Mathf.Clamp(v, 0, 255)); return this; }
        public W S8(int v) { w.Write((sbyte)Mathf.Clamp(v, -128, 127)); return this; }
        public W I16(int v) { w.Write((short)Mathf.Clamp(v, short.MinValue, short.MaxValue)); return this; }
        public W U16(int v) { w.Write((ushort)Mathf.Clamp(v, 0, 65535)); return this; }
        public W I32(int v) { w.Write(v); return this; }
        public W F(float v) { w.Write(v); return this; }
        public W V(Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); return this; }
        public W Dir(Vector3 v) { v.Normalize(); w.Write((short)(v.x * 32767)); w.Write((short)(v.y * 32767)); w.Write((short)(v.z * 32767)); return this; }
        public W Q(Quaternion q) { if (q.w < 0) { q.x = -q.x; q.y = -q.y; q.z = -q.z; q.w = -q.w; } w.Write((short)(q.x * 32767)); w.Write((short)(q.y * 32767)); w.Write((short)(q.z * 32767)); w.Write((short)(q.w * 32767)); return this; }
        public W Col(Color c) { w.Write((byte)(Mathf.Clamp01(c.r) * 255)); w.Write((byte)(Mathf.Clamp01(c.g) * 255)); w.Write((byte)(Mathf.Clamp01(c.b) * 255)); w.Write((byte)(Mathf.Clamp01(c.a) * 255)); return this; }
        public W Str(string s) { w.Write(s ?? ""); return this; }
        public byte[] Done() { w.Flush(); return ms.ToArray(); }
    }

    /// <summary>Message reader mirroring W.</summary>
    public class R
    {
        readonly BinaryReader r;
        public readonly Msg type;
        public R(byte[] b) { r = new BinaryReader(new MemoryStream(b)); type = (Msg)r.ReadByte(); }
        public bool B() => r.ReadBoolean();
        public int U8() => r.ReadByte();
        public int S8() => r.ReadSByte();
        public int I16() => r.ReadInt16();
        public int U16() => r.ReadUInt16();
        public int I32() => r.ReadInt32();
        public float F() => r.ReadSingle();
        public Vector3 V() => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        public Vector3 Dir() => new Vector3(r.ReadInt16() / 32767f, r.ReadInt16() / 32767f, r.ReadInt16() / 32767f).normalized;
        public Quaternion Q() { var q = new Quaternion(r.ReadInt16() / 32767f, r.ReadInt16() / 32767f, r.ReadInt16() / 32767f, r.ReadInt16() / 32767f); float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w); if (m < 1e-4f) return Quaternion.identity; q.x /= m; q.y /= m; q.z /= m; q.w /= m; return q; }
        public Color Col() => new Color(r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f, r.ReadByte() / 255f);
        public string Str() => r.ReadString();
    }

    /// <summary>LAN discovery: a hosting game shouts "here I am" once a second on UDP broadcast; joiners listen.</summary>
    public class Discovery
    {
        UdpClient udp;
        Thread thread;
        volatile bool run;
        public readonly Dictionary<string, (string name, string info, float seen)> found = new Dictionary<string, (string, string, float)>();
        readonly ConcurrentQueue<(string ip, string payload)> heard = new ConcurrentQueue<(string, string)>();
        static AndroidJavaObject multicastLock;

        public bool Listening { get; private set; }

        public void StartListening()
        {
            Stop();
            Listening = true;
            found.Clear();
            AcquireMulticast();
            try
            {
                udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, NetLink.DiscoveryPort));
                run = true;
                thread = new Thread(() =>
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    while (run)
                    {
                        try { var b = udp.Receive(ref ep); heard.Enqueue((ep.Address.ToString(), Encoding.UTF8.GetString(b))); }
                        catch { if (!run) break; }
                    }
                }) { IsBackground = true, Name = "net-discovery" };
                thread.Start();
            }
            catch (Exception e) { Debug.LogWarning("discovery listen failed: " + e.Message); }
        }

        /// <summary>Main thread: fold in what was heard; drop games not heard for 4 s.</summary>
        public void Update()
        {
            while (heard.TryDequeue(out var h))
            {
                var parts = h.payload.Split('|');
                if (parts.Length < 3 || parts[0] != NetLink.Version) continue;
                found[h.ip] = (parts[1], parts[2], Time.unscaledTime);
            }
            var stale = new List<string>();
            foreach (var kv in found) if (Time.unscaledTime - kv.Value.seen > 4) stale.Add(kv.Key);
            foreach (var k in stale) found.Remove(k);
        }

        public void StartAnnouncing(Func<string> payload)
        {
            Stop();
            try
            {
                udp = new UdpClient { EnableBroadcast = true };
                run = true;
                thread = new Thread(() =>
                {
                    var ep = new IPEndPoint(IPAddress.Broadcast, NetLink.DiscoveryPort);
                    while (run)
                    {
                        try { var b = Encoding.UTF8.GetBytes(NetLink.Version + "|" + payload()); udp.Send(b, b.Length, ep); } catch { }
                        Thread.Sleep(1000);
                    }
                }) { IsBackground = true, Name = "net-announce" };
                thread.Start();
            }
            catch (Exception e) { Debug.LogWarning("announce failed: " + e.Message); }
        }

        public void Stop()
        {
            run = false;
            Listening = false;
            try { udp?.Close(); } catch { }
            udp = null;
        }

        /// <summary>Android drops broadcast packets unless a multicast lock is held.</summary>
        static void AcquireMulticast()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (multicastLock != null) return;
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var wifi = act.Call<AndroidJavaObject>("getSystemService", "wifi"))
                {
                    multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "bis-discovery");
                    multicastLock.Call("acquire");
                }
            }
            catch (Exception e) { Debug.LogWarning("multicast lock: " + e.Message); }
#endif
        }
    }
}
