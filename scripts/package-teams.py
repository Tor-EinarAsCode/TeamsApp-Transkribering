#!/usr/bin/env python3
"""Render and package the internally installable Teams app; no account mutations."""
import argparse,json,re,uuid,zipfile
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--app-id',required=True);p.add_argument('--bot-id',required=True);p.add_argument('--host',required=True);p.add_argument('--output',required=True);a=p.parse_args()
for value in (a.app_id,a.bot_id):uuid.UUID(value)
if not re.fullmatch(r'[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?',a.host) or '.' not in a.host:raise SystemExit('host must be a public DNS name without scheme or path')
root=Path(__file__).resolve().parents[1];text=(root/'teams/manifest.template.json').read_text()
for token,value in [('TEAMS_APP_ID',a.app_id),('BOT_ID',a.bot_id),('APP_HOST',a.host)]:text=text.replace('{{'+token+'}}',value)
manifest=json.loads(text);out=Path(a.output);out.parent.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(out,'w',zipfile.ZIP_DEFLATED) as z:
 z.writestr('manifest.json',json.dumps(manifest,ensure_ascii=False,indent=2));z.write(root/'teams/color.png','color.png');z.write(root/'teams/outline.png','outline.png')
print(f'Created {out}. Tenant administrator must restrict installation to the internal organization.')
