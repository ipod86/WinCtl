using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace WinCtl
{
    /// <summary>Minimaler MQTT-3.1.1-Client (QoS 0/1 empfangen, QoS 0 senden, LWT, Retain, TLS, Auto-Reconnect).</summary>
    public class MqttClient
    {
        public string Host = "localhost";
        public int Port = 1883;
        public string User, Pass;
        public string ClientId = "winctl";
        public bool Tls, TlsInsecure;
        public int KeepAlive = 30;
        public string WillTopic, WillPayload;
        public bool WillRetain = true;

        public event Action<string, byte[]> OnMessage;
        public event Action OnConnected;
        public event Action OnDisconnected;

        readonly List<string> subs = new List<string>();
        TcpClient tcp; Stream stream;
        Thread th; Timer pingTimer;
        volatile bool stop; volatile bool connected;
        readonly object wl = new object();
        int pid = 1;

        public bool IsConnected { get { return connected; } }

        public void Subscribe(string filter)
        {
            lock (subs) { if (!subs.Contains(filter)) subs.Add(filter); }
            if (connected) SendSubscribe(filter);
        }

        public void Start()
        {
            stop = false;
            th = new Thread(Loop) { IsBackground = true, Name = "mqtt" };
            th.Start();
            pingTimer = new Timer(delegate { if (connected) Send(new byte[] { 0xC0, 0 }); }, null, KeepAlive * 500, KeepAlive * 500);
        }

        public void Stop()
        {
            stop = true;
            if (pingTimer != null) pingTimer.Dispose();
            try { if (connected) Send(new byte[] { 0xE0, 0 }); } catch { }
            connected = false;
            Close();
        }

        void Loop()
        {
            int backoff = 1;
            while (!stop)
            {
                try
                {
                    Connect();
                    backoff = 1;
                    connected = true;
                    lock (subs) { foreach (var s in subs) SendSubscribe(s); }
                    Log.Info("MQTT verbunden mit " + Host + ":" + Port);
                    var h = OnConnected; if (h != null) { try { h(); } catch (Exception ex) { Log.Error("OnConnected: " + ex.Message); } }
                    ReadLoop();
                }
                catch (Exception ex)
                {
                    if (!stop) Log.Error("MQTT: " + ex.Message);
                }
                bool was = connected;
                connected = false;
                Close();
                if (was && !stop) { var d = OnDisconnected; if (d != null) { try { d(); } catch { } } }
                if (stop) break;
                for (int i = 0; i < backoff * 10 && !stop; i++) Thread.Sleep(100);
                backoff = Math.Min(backoff * 2, 30);
            }
        }

        void Close()
        {
            try { if (stream != null) stream.Close(); } catch { }
            try { if (tcp != null) tcp.Close(); } catch { }
        }

        void Connect()
        {
            tcp = new TcpClient { NoDelay = true };
            var ar = tcp.BeginConnect(Host, Port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(8000)) { try { tcp.Close(); } catch { } throw new IOException("Verbindungs-Timeout zu " + Host + ":" + Port); }
            tcp.EndConnect(ar);
            tcp.ReceiveTimeout = Math.Max(10, KeepAlive) * 1500;
            Stream s = tcp.GetStream();
            if (Tls)
            {
                var ssl = new SslStream(s, false, delegate (object o, System.Security.Cryptography.X509Certificates.X509Certificate c, System.Security.Cryptography.X509Certificates.X509Chain ch, SslPolicyErrors e) { return TlsInsecure || e == SslPolicyErrors.None; });
                ssl.AuthenticateAsClient(Host);
                s = ssl;
            }
            stream = s;

            var body = new MemoryStream();
            WriteStr(body, "MQTT");
            body.WriteByte(4);
            int flags = 0x02; // clean session
            bool hasUser = !string.IsNullOrEmpty(User), hasPass = !string.IsNullOrEmpty(Pass);
            if (hasUser) flags |= 0x80;
            if (hasUser && hasPass) flags |= 0x40;
            if (WillTopic != null) { flags |= 0x04; if (WillRetain) flags |= 0x20; }
            body.WriteByte((byte)flags);
            body.WriteByte((byte)(KeepAlive >> 8)); body.WriteByte((byte)(KeepAlive & 0xFF));
            WriteStr(body, ClientId);
            if (WillTopic != null) { WriteStr(body, WillTopic); var wp = Encoding.UTF8.GetBytes(WillPayload ?? ""); body.WriteByte((byte)(wp.Length >> 8)); body.WriteByte((byte)(wp.Length & 0xFF)); body.Write(wp, 0, wp.Length); }
            if (hasUser) WriteStr(body, User);
            if (hasUser && hasPass) WriteStr(body, Pass);
            var pkt = new MemoryStream();
            pkt.WriteByte(0x10); WriteVarLen(pkt, (int)body.Length);
            var bb = body.ToArray(); pkt.Write(bb, 0, bb.Length);
            var all = pkt.ToArray(); stream.Write(all, 0, all.Length); stream.Flush();

            int type, pf; byte[] resp;
            ReadPacket(out type, out pf, out resp);
            if (type != 2 || resp.Length < 2) throw new IOException("Unerwartete Antwort auf CONNECT");
            if (resp[1] != 0)
            {
                string[] m = { "", "Protokoll abgelehnt", "Client-ID abgelehnt", "Broker nicht verfügbar", "Benutzer/Passwort falsch", "Nicht autorisiert" };
                throw new IOException("Broker lehnt Verbindung ab: " + (resp[1] < m.Length ? m[resp[1]] : "Code " + resp[1]));
            }
        }

        byte[] ReadN(int n)
        {
            var b = new byte[n]; int o = 0;
            while (o < n) { int r = stream.Read(b, o, n - o); if (r <= 0) throw new IOException("Verbindung geschlossen"); o += r; }
            return b;
        }

        void ReadPacket(out int type, out int flags, out byte[] body)
        {
            int first = stream.ReadByte();
            if (first < 0) throw new IOException("Verbindung geschlossen");
            int mult = 1, len = 0, digit, n = 0;
            do
            {
                digit = stream.ReadByte(); if (digit < 0) throw new IOException("Verbindung geschlossen");
                len += (digit & 127) * mult; mult *= 128;
                if (++n > 4) throw new IOException("Ungültige Paketlänge");
            } while ((digit & 128) != 0);
            type = first >> 4; flags = first & 0x0F;
            body = len > 0 ? ReadN(len) : new byte[0];
        }

        void ReadLoop()
        {
            while (!stop)
            {
                int type, flags; byte[] body;
                ReadPacket(out type, out flags, out body);
                if (type == 3) // PUBLISH
                {
                    int qos = (flags >> 1) & 3;
                    int tl = (body[0] << 8) | body[1];
                    string topic = Encoding.UTF8.GetString(body, 2, tl);
                    int off = 2 + tl; ushort id = 0;
                    if (qos > 0) { id = (ushort)((body[off] << 8) | body[off + 1]); off += 2; }
                    var payload = new byte[body.Length - off];
                    Buffer.BlockCopy(body, off, payload, 0, payload.Length);
                    if (qos == 1) Send(new byte[] { 0x40, 2, (byte)(id >> 8), (byte)(id & 0xFF) });
                    else if (qos == 2) Send(new byte[] { 0x50, 2, (byte)(id >> 8), (byte)(id & 0xFF) });
                    var h = OnMessage;
                    if (h != null) { try { h(topic, payload); } catch (Exception ex) { Log.Error("Message-Handler: " + ex.Message); } }
                }
                else if (type == 6) // PUBREL
                {
                    if (body.Length >= 2) Send(new byte[] { 0x70, 2, body[0], body[1] });
                }
                // PINGRESP / SUBACK / PUBACK: ignorieren
            }
        }

        void SendSubscribe(string filter)
        {
            var tb = Encoding.UTF8.GetBytes(filter);
            int id = Interlocked.Increment(ref pid) & 0xFFFF; if (id == 0) id = 1;
            var ms = new MemoryStream();
            ms.WriteByte(0x82); WriteVarLen(ms, 2 + 2 + tb.Length + 1);
            ms.WriteByte((byte)(id >> 8)); ms.WriteByte((byte)(id & 0xFF));
            ms.WriteByte((byte)(tb.Length >> 8)); ms.WriteByte((byte)(tb.Length & 0xFF));
            ms.Write(tb, 0, tb.Length); ms.WriteByte(1);
            Send(ms.ToArray());
        }

        public bool Publish(string topic, string payload, bool retain)
        {
            return Publish(topic, Encoding.UTF8.GetBytes(payload ?? ""), retain);
        }

        public bool Publish(string topic, byte[] payload, bool retain)
        {
            if (!connected) return false;
            var tb = Encoding.UTF8.GetBytes(topic);
            var ms = new MemoryStream(payload.Length + tb.Length + 16);
            ms.WriteByte((byte)(0x30 | (retain ? 1 : 0)));
            WriteVarLen(ms, 2 + tb.Length + payload.Length);
            ms.WriteByte((byte)(tb.Length >> 8)); ms.WriteByte((byte)(tb.Length & 0xFF));
            ms.Write(tb, 0, tb.Length); ms.Write(payload, 0, payload.Length);
            return Send(ms.ToArray());
        }

        bool Send(byte[] data)
        {
            lock (wl)
            {
                try { var s = stream; if (s == null) return false; s.Write(data, 0, data.Length); s.Flush(); return true; }
                catch { try { if (tcp != null) tcp.Close(); } catch { } return false; }
            }
        }

        static void WriteStr(Stream s, string v)
        {
            var b = Encoding.UTF8.GetBytes(v ?? "");
            s.WriteByte((byte)(b.Length >> 8)); s.WriteByte((byte)(b.Length & 0xFF)); s.Write(b, 0, b.Length);
        }

        static void WriteVarLen(Stream s, int len)
        {
            do { int d = len % 128; len /= 128; if (len > 0) d |= 128; s.WriteByte((byte)d); } while (len > 0);
        }
    }
}
