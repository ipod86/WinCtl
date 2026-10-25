#!/usr/bin/env python3
"""Erzeugt docs/COMMANDS.md aus docs/protocol.json (Ausgabe von `WinCtl.exe --dump-protocol`)."""
import json, os
root = os.path.join(os.path.dirname(__file__), '..')
d = json.load(open(os.path.join(root, 'docs', 'protocol.json'), encoding='utf-8'))
cats = {}
for c in d['commands']: cats.setdefault(c['category'], []).append(c)
names = {'system':'System','power':'Energie & Sitzung','programs':'Programme & Fenster','audio':'Audio','display':'Anzeige','input':'Eingabe'}
out = ['# Befehlsreferenz', '', f"_Automatisch erzeugt aus `WinCtl.exe --dump-protocol` (Protokoll {d['protocol']}, Version {d['version']}). Nicht von Hand ändern._", '',
       'Befehle werden an `{base}/{id}/cmd/<befehl>` gesendet (Payload: JSON-Objekt oder - bei Befehlen mit *Kurzform* - ein einfacher String).', '']
for cat in ['system','power','programs','audio','display','input']:
    if cat not in cats: continue
    out += [f'## {names[cat]}', '']
    for c in cats[cat]:
        flags = []
        if c.get('dangerous'): flags.append('⚠ sicherheitsrelevant')
        if c.get('needs_gui'): flags.append('braucht GUI-Sitzung')
        out.append(f"### `{c['name']}`" + (f"  _({', '.join(flags)})_" if flags else ''))
        out.append(c.get('description_de', c['description']) + '  ')
        if c.get('primary'): out.append(f"Kurzform: Payload `<text>` entspricht `{{\"{c['primary']}\": \"<text>\"}}`.  ")
        ex = [e for e in c.get('examples', []) if e != 'true']
        if ex:
            out += ['', 'Beispiele:', '', '```'] + ex + ['```']
        if c['params']:
            out += ['', '| Parameter | Typ | Pflicht | Standard | Beschreibung |', '|---|---|---|---|---|']
            for p in c['params']:
                desc = p['description'] + (' (' + ' / '.join(p['enum']) + ')' if p.get('enum') else '')
                dv = json.dumps(p['default'], ensure_ascii=False) if 'default' in p else ''
                out.append(f"| `{p['name']}` | {p['type']} | {'ja' if p['required'] else ''} | {dv} | {desc} |")
        out.append('')
out += ['## States', '', '| Topic (`{base}/{id}/state/…`) | Typ | Einheit | Rolle | Beschreibung |', '|---|---|---|---|---|']
for s in d['states']: out.append(f"| `{s['name']}` | {s['type']} | {s['unit']} | `{s['role']}` | {s.get('description_de', s['description'])} |")
open(os.path.join(root, 'docs', 'COMMANDS.md'), 'w', encoding='utf-8').write('\n'.join(out) + '\n')
print('docs/COMMANDS.md geschrieben')
