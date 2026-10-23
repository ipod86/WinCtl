#!/usr/bin/env python3
"""Protokoll-Smoketest gegen einen laufenden Agent.
Aufruf:  python tools/smoke_test.py --host 192.168.1.10 --id mein-pc [--user u --password p]
Benötigt: pip install paho-mqtt
Testet: online/info/state, ping mit Korrelations-ID, unbekannter Befehl, Whitelist-Block, Broadcast.
"""
import argparse, json, sys, time
import paho.mqtt.client as mqtt

ap = argparse.ArgumentParser()
ap.add_argument('--host', default='localhost'); ap.add_argument('--port', type=int, default=1883)
ap.add_argument('--user'); ap.add_argument('--password'); ap.add_argument('--base', default='winctl'); ap.add_argument('--id', required=True)
a = ap.parse_args()

got, results = {}, []
def on_msg(c, u, m):
    got[m.topic] = m.payload
    if m.topic.endswith('/result'): results.append(json.loads(m.payload))
c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2) if hasattr(mqtt, 'CallbackAPIVersion') else mqtt.Client()
if a.user: c.username_pw_set(a.user, a.password)
c.on_message = on_msg; c.connect(a.host, a.port); c.subscribe(f'{a.base}/#'); c.loop_start(); time.sleep(2)

T = f'{a.base}/{a.id}'
def cmd(name, payload=''):
    n = len(results); c.publish(f'{T}/cmd/{name}', payload); t = time.time()
    while len(results) == n and time.time() - t < 3: time.sleep(0.05)
    return results[-1] if len(results) > n else None

fails = 0
def check(label, cond):
    global fails; print(('OK   ' if cond else 'FAIL ') + label); fails += 0 if cond else 1

check('online = true', got.get(f'{T}/online') == b'true')
info = json.loads(got.get(f'{T}/info', b'{}') or b'{}')
check('info enthält Befehle und States', len(info.get('commands', [])) > 10 and len(info.get('states', [])) > 5)
check('mindestens ein state/* Topic', any('/state/' in k for k in got))
r = cmd('ping', '{"id":"smoke-1"}'); check('ping ok + Korrelations-ID', bool(r) and r['ok'] and r['id'] == 'smoke-1')
r = cmd('gibtsnicht'); check('unbekannter Befehl -> ok=false', bool(r) and not r['ok'])
r = cmd('run', 'C:\\\\Windows\\\\System32\\\\format.com'); check('Whitelist blockiert fremden Pfad', bool(r) and not r['ok'] and 'Alias' in (r.get('error') or ''))
n = len(results); c.publish(f'{a.base}/all/cmd/ping', ''); time.sleep(1.5); check('Broadcast-Ping beantwortet', len(results) > n)
print('\nErgebnis:', 'ALLES OK' if not fails else f'{fails} Fehler'); sys.exit(1 if fails else 0)
