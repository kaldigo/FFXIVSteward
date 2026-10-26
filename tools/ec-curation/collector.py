import http.server,json,pathlib
import argparse
parser=argparse.ArgumentParser(description="Local browser-form collector for public EC evidence; never crawls or reads player data.")
parser.add_argument("work_dir",type=pathlib.Path)
parser.add_argument("--port",type=int,default=8769)
args=parser.parse_args();ROOT=args.work_dir.resolve();ROOT.mkdir(parents=True,exist_ok=True)
class Handler(http.server.BaseHTTPRequestHandler):
 def do_GET(self):
  if self.path.split('?')[0] in ['/metadata','/discovery','/observations','/counts','/catalogue']:
   name=self.path.split('?')[0][1:]
   file=ROOT/(name+'.json');data=file.read_bytes() if file.exists() else json.dumps({'kind':name,'rows':[]}).encode();kind='application/json'
  else:
   data=b'<title>Local curation evidence</title><h1>Save public curation evidence locally</h1><form method="post"><label>Evidence JSON<textarea name="evidence" rows="12" cols="100"></textarea></label><button>Save evidence</button></form>';kind='text/html'
  self.send_response(200);self.send_header('Content-Type',kind+'; charset=utf-8');self.end_headers();self.wfile.write(data)
 def do_POST(self):
  import urllib.parse
  size=int(self.headers['Content-Length'])
  if size>20_000_000: self.send_error(413);return
  value=json.loads(urllib.parse.parse_qs(self.rfile.read(size).decode())['evidence'][0])
  name=value['kind']
  if name not in {'discovery','observations','counts','catalogue'}: self.send_error(400);return
  (ROOT/(name+'.json')).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
  self.send_response(200);self.end_headers();self.wfile.write(('Saved '+name).encode())
 def log_message(self,*args):pass
print(f'Local public-evidence collector listening on 127.0.0.1:{args.port}',flush=True)
http.server.HTTPServer(('127.0.0.1',args.port),Handler).serve_forever()
