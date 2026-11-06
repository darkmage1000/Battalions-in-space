using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace BIS.Net
{
    /// <summary>
    /// Same-Wi-Fi multiplayer for two people. The host runs the whole battle exactly as in single player, with one
    /// extra human participant (Participant.remote) whose inputs arrive over the network. The guest builds the
    /// same map, draws copies ("replicas") of every unit, vehicle, post and capital ship from the host's
    /// snapshots (20 a second) and events (spawns, deaths, every shot, kill feed...), and sends its own
    /// UnitInput / VehicleInput every frame (30 a second). Spawning, entering / leaving vehicles and changing
    /// seat are requests the host checks with its own rules.
    /// </summary>
    public static class Session
    {
        public enum Role { None, Host, Client }
        public static Role role;
        public static NetLink link;
        public static readonly Discovery disco = new Discovery();
        public static bool Active => role != Role.None;
        public static bool IsHost => role == Role.Host;
        public static bool IsClient => role == Role.Client;
        public static string localName = DefaultName();
        public static string peerName;
        public static bool together = true;           // host's choice: the guest joins the host's side or the other one
        public static string status = "";
        public static string lobbyInfo = "";          // host's map / mode, shown to the guest
        static TcpListener listener;
        static Thread acceptThread;
        static volatile TcpClient pending;

        static string DefaultName()
        {
            string n = SystemInfo.deviceName;
            if (string.IsNullOrEmpty(n) || n == "<unknown>") n = "Player";
            return n.Length > 18 ? n.Substring(0, 18) : n;
        }

        // ── host lobby ───────────────────────────────────────────────────
        public static void HostLobby(System.Func<string> info)
        {
            Shutdown();
            role = Role.Host;
            peerName = null;
            status = "Waiting for someone to join...";
            try
            {
                listener = new TcpListener(IPAddress.Any, NetLink.Port);
                listener.Start();
                acceptThread = new Thread(() =>
                {
                    try
                    {
                        while (role == Role.Host && listener != null)
                        {
                            var c = listener.AcceptTcpClient();
                            if (link != null && !link.Closed) { c.Close(); continue; }    // one guest at a time
                            pending = c;
                        }
                    }
                    catch { }
                }) { IsBackground = true, Name = "net-accept" };
                acceptThread.Start();
            }
            catch (System.Exception e) { status = "Could not open the game: " + e.Message; }
            disco.StartAnnouncing(() => localName + "|" + info() + (peerName != null ? " (full)" : ""));
        }

        /// <summary>Main thread, while hosting: take a new connection and read the lobby messages.</summary>
        public static void HostLobbyUpdate()
        {
            if (pending != null) { link = new NetLink(pending); pending = null; }
            if (link == null) return;
            if (link.Closed) { link = null; peerName = null; status = "The guest left. Waiting for someone to join..."; return; }
            while (link.TryRead(out var m))
            {
                var r = new R(m);
                if (r.type == Msg.Hello)
                {
                    string ver = r.Str(); peerName = r.Str();
                    if (ver != NetLink.Version) { link.Send(new W(Msg.Bye).Str("Different game version")); link.Close(); link = null; peerName = null; continue; }
                    status = peerName + " joined";
                    link.Send(new W(Msg.Lobby).Str(localName));
                }
                else if (r.type == Msg.Bye) { link.Close(); link = null; peerName = null; status = "The guest left. Waiting for someone to join..."; }
            }
        }

        // ── guest lobby ──────────────────────────────────────────────────
        public static void StartBrowsing() { Shutdown(); disco.StartListening(); status = "Looking for games on this Wi-Fi..."; }

        public static bool Join(string ip)
        {
            disco.Stop();
            status = "Connecting to " + ip + "...";
            var l = NetLink.Connect(ip);
            if (l == null) { status = "Could not reach " + ip + " (same Wi-Fi? is the host waiting?)"; disco.StartListening(); return false; }
            link = l; role = Role.Client;
            link.Send(new W(Msg.Hello).Str(NetLink.Version).Str(localName));
            status = "Connected - waiting for the host to start";
            return true;
        }

        /// <summary>Main thread, while joined and waiting: lobby / start messages.</summary>
        public static void ClientLobbyUpdate()
        {
            if (link == null) return;
            if (link.Closed) { status = "Lost the connection to the host"; role = Role.None; link = null; return; }
            while (link.TryRead(out var m))
            {
                var r = new R(m);
                switch (r.type)
                {
                    case Msg.Lobby: peerName = r.Str(); status = "Connected to " + peerName + " - waiting for them to start"; break;
                    case Msg.Start: NetClient.Begin(r); return;
                    case Msg.Bye: status = "The host closed the game: " + r.Str(); link.Close(); link = null; role = Role.None; return;
                }
            }
        }

        public static void Shutdown()
        {
            if (link != null && !link.Closed) { try { link.Send(new W(Msg.Bye).Str("left")); } catch { } Thread.Sleep(30); link.Close(); }
            link = null;
            try { listener?.Stop(); } catch { }
            listener = null; pending = null;
            disco.Stop();
            role = Role.None;
            NetHost.Reset(); NetClient.Reset();
        }
    }
}
