// Polite crawl of NCSoft Taiwan's public AION 2 item dictionary (English locale).
// One request every 400 ms, resumable, writes tw-items.json next to this script.
// Run by the site owner: `node tools/crawl-tw.js`, then move tw-items.json to data/.
// Note: NCSoft's EULA restricts automated access; running this is the owner's decision.
var https = require('https'), fs = require('fs'), path = require('path');
var BASE = 'https://tw.ncsoft.com/aion2_tw/v2.0';
var UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) KinahLedger-research/0.1 (contact: site owner)';
var OUT = path.join(__dirname, 'tw-items.json'), PROG = path.join(__dirname, 'tw-progress.json');
var DELAY = 400, SIZE = 50;

function get(url){
  return new Promise(function(res, rej){
    https.get(url, {headers:{'User-Agent':UA, 'Accept':'application/json'}}, function(r){
      var b = ''; r.on('data', function(d){ b += d; });
      r.on('end', function(){ if (r.statusCode !== 200) return rej(new Error('HTTP ' + r.statusCode + ' ' + url)); try { res(JSON.parse(b)); } catch(e){ rej(new Error('bad json ' + url)); } });
    }).on('error', rej);
  });
}
function sleep(ms){ return new Promise(function(r){ setTimeout(r, ms); }); }
async function getRetry(url){ for (var i = 0; i < 4; i++){ try { return await get(url); } catch(e){ if (i === 3) throw e; await sleep(1500 * (i + 1)); } } }

(async function(){
  var items = {}, done = {};
  if (fs.existsSync(OUT)) JSON.parse(fs.readFileSync(OUT, 'utf8')).items.forEach(function(i){ items[i.id] = i; });
  if (fs.existsSync(PROG)) done = JSON.parse(fs.readFileSync(PROG, 'utf8'));
  var cats = await getRetry(BASE + '/game/item/category?locale=en-US');
  var reqs = 0, t0 = Date.now();
  for (var ci = 0; ci < cats.length; ci++){
    var c1 = cats[ci], subs = (c1.child && c1.child.length) ? c1.child : [null];
    for (var si = 0; si < subs.length; si++){
      var c2 = subs[si], key = c1.id + '/' + (c2 ? c2.id : '-');
      if (done[key]) continue;
      var page = 1, n = 0;
      for (;;){
        var url = BASE + '/dict/search/item?page=' + page + '&size=' + SIZE + '&locale=en-US&category1=' + encodeURIComponent(c1.id) + (c2 ? '&category2=' + encodeURIComponent(c2.id) : '');
        var data = await getRetry(url); reqs++;
        var rows = data.contents || [];
        rows.forEach(function(r){
          items[r.id] = {id:r.id, name:r.name, grade:r.grade, tradable:r.tradable, cat1:c1.id, cat1Name:c1.name, cat2:c2 ? c2.id : null, cat2Name:c2 ? c2.name : null, categoryName:r.categoryName, icon:r.image, description:r.description || '', options:r.options || []};
        });
        n += rows.length;
        var last = data.pagination ? data.pagination.lastPage : 1;
        if (!rows.length || page >= last) break;
        page++; await sleep(DELAY);
      }
      done[key] = n;
      fs.writeFileSync(PROG, JSON.stringify(done));
      fs.writeFileSync(OUT, JSON.stringify({source:'tw.ncsoft.com/aion2_tw/v2.0 item dictionary, locale en-US', fetched:new Date().toISOString(), count:Object.keys(items).length, items:Object.keys(items).map(function(k){ return items[k]; })}));
      console.log(key.padEnd(34), String(n).padStart(5), 'items', '| pages', page, '| total so far', Object.keys(items).length);
      await sleep(DELAY);
    }
  }
  console.log('done:', Object.keys(items).length, 'items,', reqs, 'requests,', Math.round((Date.now() - t0) / 1000) + 's');
})().catch(function(e){ console.error('FAILED:', e.message); process.exit(1); });
