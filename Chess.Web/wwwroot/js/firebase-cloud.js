// The cloud courier's JS half: anonymous auth, one game subscription, one append.
//
// An ES module, so it can be reached two ways without duplicating anything: `JSHost.ImportAsync`
// from the .NET side (CloudCourier.cs), and a plain `import` from `firebase/cloud-check.html`,
// which is the two-tab walking skeleton that proves push works before any of it is wired into the
// app.
//
// The SDK itself is loaded from gstatic at a PINNED version rather than bundled. It is ~100 KB of
// JS that has nothing to do with the WASM payload, it is only fetched when a config exists, and
// pinning keeps a silent upstream change out of a build that is otherwise reproducible.
//
// NOTHING HERE KNOWS CHESS. `g` is GameLinkCodec's payload and `n` is its ply count; this module
// moves that string and never reads it. The rules are the boundary (see
// firebase/database.rules.json): they enforce append-only, the turn gate, and seat ownership, so a
// bug here cannot corrupt a game, only fail to show one.

const SDK = 'https://www.gstatic.com/firebasejs/11.10.0';

let db = null;      // the Database handle, null while the cloud is disabled
let fns = null;     // the database functions we use, captured from the dynamic import
let uid = '';       // our anonymous uid, '' until signed in
const watches = new Map();  // gameId -> unsubscribe
const OPEN = '*open';      // the lobby's key in `watches`; '*' is not a legal game id, so no clash

/**
 * Load the SDK, sign in anonymously, and return the uid. Returns '' when the cloud is disabled,
 * which is the normal state of a fork and of any build with no config: `configJson` is empty, and
 * NOTHING is fetched -- no SDK, no network, no error. The caller treats '' as "no cloud" and every
 * other entry point here becomes a no-op.
 */
export async function init(configJson) {
  if (db) return uid;
  if (!configJson) return '';

  const cfg = JSON.parse(configJson);

  const { initializeApp } = await import(`${SDK}/firebase-app.js`);
  const authMod = await import(`${SDK}/firebase-auth.js`);
  const dbMod = await import(`${SDK}/firebase-database.js`);

  // The emulator block is how this whole path is testable with no secrets and no live project:
  // `firebase emulators:start` plus a config carrying only a demo projectId. Absent in any real
  // config, so a production build cannot accidentally take this branch.
  //
  // Point `databaseURL` at the emulator's host:port and it SILENTLY MISBEHAVES rather than failing:
  // the SDK takes the database NAMESPACE from the first label of that host, so `127.0.0.1:9000`
  // reads as namespace `127`, the emulator creates it on demand with default (open) rules, and
  // every write succeeds against a database that is not the one under test. Two clients both
  // "created" the same game before this was caught. So give the SDK a well-formed URL for the
  // namespace and hand it the address separately, which is what connectDatabaseEmulator is for.
  const emu = cfg.emulator;
  const app = initializeApp(emu
    ? { projectId: cfg.projectId, apiKey: 'demo',
        databaseURL: `https://${cfg.projectId}-default-rtdb.firebaseio.com` }
    : cfg);

  const auth = authMod.getAuth(app);
  if (emu) authMod.connectAuthEmulator(auth, emu.auth, { disableWarnings: true });

  db = dbMod.getDatabase(app);
  if (emu) dbMod.connectDatabaseEmulator(db, emu.host, emu.port);
  fns = dbMod;

  const cred = await authMod.signInAnonymously(auth);
  uid = cred.user.uid;
  return uid;
}

/** Our anonymous uid, or '' when the cloud is disabled. */
export function currentUid() {
  return uid;
}

/**
 * Subscribe to one game. `onChange` is handed the raw row as JSON -- `{g, n, w, b}` -- on every
 * change including the first, and `null` when the game does not exist or is not ours to read.
 *
 * Subscribing twice to the same game replaces the first subscription rather than stacking, because
 * a duplicate would deliver every ply twice to a caller with no way to tell.
 */
export function watch(gameId, onChange) {
  if (!db) return;
  unwatch(gameId);

  const off = fns.onValue(
    fns.ref(db, `games/${gameId}`),
    (snap) => onChange(snap.exists() ? JSON.stringify(snap.val()) : null),
    // A permission error is a legitimate answer here ("not your game"), not a crash: report it the
    // same way as absence so the caller has one case to handle.
    () => onChange(null));

  watches.set(gameId, off);
}

export function unwatch(gameId) {
  const off = watches.get(gameId);
  if (off) { off(); watches.delete(gameId); }
}

/**
 * Append a ply: write the whole move log and the new ply count together. The rules reject anything
 * that is not an extension of what is stored (`beginsWith`) by a player whose turn it is, so a
 * stale client loses the race cleanly instead of rewriting history -- which is why this can be a
 * plain update rather than a transaction.
 *
 * Returns true on success, false when the rules refused it.
 */
export async function append(gameId, g, n) {
  if (!db) return false;
  try {
    await fns.update(fns.ref(db, `games/${gameId}`), { g, n });
    return true;
  } catch {
    return false;
  }
}

/**
 * Advertise a game with an empty seat. Keyed by OUR uid, which makes "one open game per player" a
 * property of the path rather than a count the rules cannot do.
 *
 * <p>Deliberately NOT cleaned up by onDisconnect(), which is how a presence table would do it. A
 * posted correspondence game has to outlive the tab that posted it -- the whole point is that
 * somebody claims it while you are away -- so it is durable, carries the time it was posted, and
 * the client hides rows that have gone stale. A live-play lobby would want the opposite.</p>
 */
export async function post(gameId, name, color) {
  if (!db) return false;
  try {
    await fns.set(fns.ref(db, `open/${uid}`),
      { gameId, name, color, updated: fns.serverTimestamp() });
    return true;
  } catch {
    return false;
  }
}

/** Withdraw our open game: someone took the seat, or we changed our mind. */
export async function unpost() {
  if (!db) return false;
  try {
    await fns.remove(fns.ref(db, `open/${uid}`));
    return true;
  } catch {
    return false;
  }
}

/**
 * Watch the lobby. `onList` receives a JSON array of rows, each with the poster's uid folded in as
 * `host`, on every change including the first. The lobby is readable by any signed-in player --
 * that is what a lobby is for -- so this is the one subscription that is not about our own games.
 */
export function watchOpen(onList) {
  if (!db) return;
  unwatchOpen();

  const off = fns.onValue(
    fns.ref(db, 'open'),
    (snap) => {
      const rows = [];
      snap.forEach((child) => { rows.push({ host: child.key, ...child.val() }); });
      onList(JSON.stringify(rows));
    },
    () => onList('[]'));

  watches.set(OPEN, off);
}

export function unwatchOpen() {
  unwatch(OPEN);
}

/** Create a game with us in one seat. Returns true on success. */
export async function create(gameId, color, name) {
  if (!db) return false;
  const seat = color === 'w' ? 'w' : 'b';
  try {
    await fns.set(fns.ref(db, `games/${gameId}`), { g: '', n: 0, [seat]: { uid, name } });
    return true;
  } catch {
    return false;
  }
}

/**
 * Take the empty seat of a game someone else opened -- the cloud's Accept, and the only terminal
 * transition the lobby has. The rules gate it (`".write": "!data.exists()"` plus "the uid must be
 * your own"), so two clients racing for one seat resolve server-side and the loser gets false.
 */
export async function claim(gameId, color, name) {
  if (!db) return false;
  const seat = color === 'w' ? 'w' : 'b';
  try {
    await fns.set(fns.ref(db, `games/${gameId}/${seat}`), { uid, name });
    return true;
  } catch {
    return false;
  }
}
