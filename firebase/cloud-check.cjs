// The automated half of cloud-check.html: drive it in TWO browser contexts and assert that a ply
// played in one arrives in the other without a reload.
//
//   npm run check:cloud          (from firebase/, with the emulators and a static server running)
//
// Two CONTEXTS rather than two tabs is the whole point: the SDK persists anonymous sign-in per
// origin, so two tabs in one profile are one player holding both seats -- which proves push and
// nothing about the rules. Separate contexts get separate storage, hence two uids, hence a real
// seat claim and a real turn gate.
//
// It borrows the Playwright driver that Chess.Web.E2E.Tests already installs, rather than adding a
// browser download to this folder. On win-arm64 there is no bundled Chromium, so the msedge channel
// is used -- the same arrangement (and the same reason) as CHESS_E2E_CHANNEL in that project.

const path = require('node:path');
const fs = require('node:fs');

const DRIVER = path.join(__dirname, '..', 'Chess.Web.E2E.Tests', 'bin', 'Debug', 'net10.0',
                         '.playwright', 'package', 'index.js');
if (!fs.existsSync(DRIVER)) {
  console.error('Playwright driver not found at\n  ' + DRIVER +
                '\nBuild the E2E project once to install it:\n  dotnet build Chess.Web.E2E.Tests');
  process.exit(2);
}
const { chromium } = require(DRIVER);

const CHANNEL = process.env.CHESS_E2E_CHANNEL || 'msedge';
const gid = 'skel' + Date.now();

// Serve the repo root ourselves, on an ephemeral port, so this needs nothing running but the
// emulators. file:// would be simpler and does not work: the ES module import and the config fetch
// are both subject to CORS, and a file:// origin fails both.
const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json' };
function serveRepoRoot() {
  const http = require('node:http');
  const root = path.join(__dirname, '..');
  const server = http.createServer((req, res) => {
    const rel = decodeURIComponent(req.url.split('?')[0]).replace(/^[/]+/, '');
    const file = path.join(root, rel);
    // Refuse anything that climbs out of the repo -- this is a dev server, but it is still a server.
    if (!file.startsWith(root) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
      res.writeHead(404).end('not found');
      return;
    }
    res.writeHead(200, { 'Content-Type': TYPES[path.extname(file)] || 'application/octet-stream' });
    fs.createReadStream(file).pipe(res);
  });
  return new Promise((ok) => server.listen(0, '127.0.0.1', () => ok(server)));
}

const txt = (page, sel) => page.$eval(sel, (e) => e.textContent.trim());

async function until(page, sel, pred, what, ms = 15000) {
  const t0 = Date.now();
  for (;;) {
    const v = await txt(page, sel).catch(() => null);
    if (v !== null && pred(v)) return v;
    if (Date.now() - t0 > ms) throw new Error(`TIMEOUT waiting for ${what}; ${sel} = ${JSON.stringify(v)}`);
    await new Promise((r) => setTimeout(r, 150));
  }
}

(async () => {
  const server = await serveRepoRoot();
  const ORIGIN = `http://127.0.0.1:${server.address().port}`;
  const URL = `${ORIGIN}/firebase/cloud-check.html`;

  const browser = await chromium.launch({ channel: CHANNEL, headless: true });
  const newTab = async (name) => {
    const page = await (await browser.newContext()).newPage();
    page.on('pageerror', (e) => console.log(`  ${name} pageerror: ${e.message}`));
    return page;
  };

  console.log(`game id: ${gid}   origin: ${ORIGIN}`);

  const A = await newTab('A');
  await A.goto(`${URL}?g=${gid}`);
  await until(A, '#seat', (v) => v === 'White', 'A to create the game and take White');
  const uidA = await txt(A, '#uid');
  console.log(`PASS  A signed in anonymously and took White    ${uidA.slice(0, 10)}…`);
  console.log(`      backend: ${await txt(A, '#backend')}`);

  const B = await newTab('B');
  await B.goto(`${URL}?g=${gid}`);
  await until(B, '#seat', (v) => v === 'Black', 'B to claim the empty Black seat');
  const uidB = await txt(B, '#uid');
  console.log(`PASS  B signed in anonymously and claimed Black ${uidB.slice(0, 10)}…`);

  if (uidA === uidB) throw new Error('the two contexts share a uid — this proves nothing');
  console.log('PASS  the two contexts really are two players');

  if (!(await B.$eval('#play', (e) => e.disabled))) throw new Error("B could move on White's turn");
  console.log("PASS  Black cannot move on White's turn");

  await A.click('#play');
  await until(B, '#n', (v) => v === '1', 'the ply to PUSH to B');
  const gB = await txt(B, '#g');
  if (gB !== 'e2e4') throw new Error(`B received ${JSON.stringify(gB)}`);
  console.log(`PASS  A's ply reached B by push, no reload      g="${gB}"`);

  await B.click('#play');
  await until(A, '#n', (v) => v === '2', 'the reply to PUSH back to A');
  const gA = await txt(A, '#g');
  if (gA !== 'e2e4.e7e5') throw new Error(`A received ${JSON.stringify(gA)}`);
  console.log(`PASS  B's reply reached A by push               g="${gA}"`);

  // A third identity must be refused the seat AND the read -- the rules, exercised from browser
  // code rather than from the emulator harness's synthetic auth.
  const C = await newTab('C');
  await C.goto(`${URL}?g=${gid}`);
  await until(C, '#log', (v) => v.includes('spectator'), 'C to be refused a seat');
  await until(C, '#g', (v) => v === '—' || v === '(no plies yet)', 'C to be refused the read');
  console.log('PASS  a third player got neither a seat nor the game');

  await browser.close();
  server.close();
  console.log('\nALL PASS');
})().catch((e) => { console.error('\nFAIL: ' + e.message); process.exit(1); });
