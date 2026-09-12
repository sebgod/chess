// Tests for database.rules.json, run against the Firebase emulator.
//
// These exist because the rules are the ONLY piece of the correspondence design that does not live
// in the C# tree, and the one part a client cannot re-check for itself: a client that believes the
// history is append-only is no use if the server disagrees. They are deliberately not part of the
// dotnet suite -- they need the emulator and a JVM, which CI for the game does not have.
//
//   npm install          (once)
//   npm test             (starts the emulator, runs this, shuts it down)
//
// JDK TRAP: firebase-tools needs Java 21+, and this repo's Android head pins JDK **17** (a newer JDK
// breaks the SDL3-CS.Android build). Do NOT "fix" the machine's JAVA_HOME to satisfy the emulator --
// that trades a working emulator for a broken Chess.Droid. Put a 21+ JDK on PATH for this command
// only. firebase-tools resolves `java` from PATH and ignores JAVA_HOME, so:
//
//   PATH="/c/Program Files/Microsoft/jdk-25.0.2.10-hotspot/bin:$PATH" npm test
//
// No Firebase account, project or login is involved: the "demo-" project id prefix makes the
// emulator run fully offline.

import { readFileSync } from 'node:fs';
import test, { after, before, beforeEach } from 'node:test';
import assert from 'node:assert/strict';

import { initializeTestEnvironment, assertFails, assertSucceeds } from '@firebase/rules-unit-testing';
import { ref, set, get, update } from 'firebase/database';

let env;

const ALICE = 'alice';
const BOB = 'bob';
const MALLORY = 'mallory';

/** A game with Alice on White and Bob on Black, written past the rules for test setup. */
async function seedGame(id, { g = '', n = 0 } = {}) {
  await env.withSecurityRulesDisabled(async (ctx) => {
    await set(ref(ctx.database(), `games/${id}`), {
      g,
      n,
      w: { uid: ALICE, name: 'Alice' },
      b: { uid: BOB, name: 'Bob' },
    });
  });
}

const dbFor = (uid) => env.authenticatedContext(uid).database();

before(async () => {
  env = await initializeTestEnvironment({
    projectId: 'demo-chess',
    database: {
      host: '127.0.0.1',
      port: 9000,
      rules: readFileSync(new URL('./database.rules.json', import.meta.url), 'utf8'),
    },
  });
});

after(async () => { await env?.cleanup(); });
beforeEach(async () => { await env.clearDatabase(); });

// ── The rule the whole design leans on ────────────────────────────

test('a move may EXTEND the history', async () => {
  await seedGame('g1', { g: 'e2e4', n: 1 });

  // Black's turn (n is odd), so Bob is the one allowed to move.
  await assertSucceeds(update(ref(dbFor(BOB), 'games/g1'), { g: 'e2e4.e7e5', n: 2 }));
});

test('a write may NOT rewrite history', async () => {
  await seedGame('g1', { g: 'e2e4.e7e5', n: 2 });

  // Alice's turn, but she tries to replace Black's reply with a different one.
  await assertFails(update(ref(dbFor(ALICE), 'games/g1'), { g: 'e2e4.c7c5', n: 3 }));
});

test('a write may NOT truncate history', async () => {
  // The griefer's move: roll the game back to a position you liked better.
  await seedGame('g1', { g: 'e2e4.e7e5', n: 2 });

  await assertFails(update(ref(dbFor(ALICE), 'games/g1'), { g: 'e2e4', n: 1 }));
});

// ── Turn gating, expressed in arithmetic rather than chess ────────

test('the side NOT to move cannot move', async () => {
  await seedGame('g1', { g: 'e2e4', n: 1 }); // odd n: Black's turn

  await assertFails(update(ref(dbFor(ALICE), 'games/g1'), { g: 'e2e4.d2d4', n: 2 }));
});

test('a player cannot play both sides by jumping the ply count', async () => {
  await seedGame('g1', { g: 'e2e4', n: 1 });

  await assertFails(update(ref(dbFor(BOB), 'games/g1'), { g: 'e2e4.e7e5.g1f3', n: 3 }));
});

// ── Who may touch a game at all ───────────────────────────────────

test('a stranger can neither read nor write someone else\'s game', async () => {
  await seedGame('g1', { g: 'e2e4', n: 1 });

  await assertFails(get(ref(dbFor(MALLORY), 'games/g1')));
  await assertFails(update(ref(dbFor(MALLORY), 'games/g1'), { g: 'e2e4.e7e5', n: 2 }));
});

test('an unauthenticated client gets nothing', async () => {
  await seedGame('g1', { g: 'e2e4', n: 1 });

  const anon = env.unauthenticatedContext().database();
  await assertFails(get(ref(anon, 'games/g1')));
  await assertFails(set(ref(anon, 'games/g2'), { g: '', n: 0, w: { uid: 'x' } }));
});

// ── Claiming a seat ───────────────────────────────────────────────

test('an empty seat can be claimed, and only for yourself', async () => {
  await env.withSecurityRulesDisabled(async (ctx) => {
    await set(ref(ctx.database(), 'games/g1'), { g: '', n: 0, w: { uid: ALICE, name: 'Alice' } });
  });

  // Mallory cannot claim the seat in someone else's name...
  await assertFails(set(ref(dbFor(MALLORY), 'games/g1/b'), { uid: BOB, name: 'Bob' }));
  // ...but Bob can claim it as himself.
  await assertSucceeds(set(ref(dbFor(BOB), 'games/g1/b'), { uid: BOB, name: 'Bob' }));
});

test('a taken seat cannot be stolen', async () => {
  await seedGame('g1');

  await assertFails(set(ref(dbFor(MALLORY), 'games/g1/b'), { uid: MALLORY, name: 'M' }));
});

// ── Nowhere to put anything ───────────────────────────────────────

test('an unknown field is rejected, so the database is not a pastebin', async () => {
  await seedGame('g1', { g: 'e2e4', n: 1 });

  await assertFails(update(ref(dbFor(BOB), 'games/g1'), { payload: 'x'.repeat(1000) }));
});

test('the move log is capped', async () => {
  await seedGame('g1');

  await assertFails(update(ref(dbFor(ALICE), 'games/g1'), { g: 'e'.repeat(30000), n: 1 }));
});

test('a display name is capped', async () => {
  await assertFails(set(ref(dbFor(ALICE), `players/${ALICE}`), { name: 'A'.repeat(64) }));
  await assertSucceeds(set(ref(dbFor(ALICE), `players/${ALICE}`), { name: 'Alice' }));
});

test('a player may only write their own profile', async () => {
  await assertFails(set(ref(dbFor(MALLORY), `players/${ALICE}`), { name: 'not Alice' }));
});

// ── Invites: the cloud handshake, shaped like LAN's ────────────────

test('an invite goes to the invitee, and only in your own name', async () => {
  await assertSucceeds(
    set(ref(dbFor(ALICE), `invites/${BOB}/${ALICE}`), { gameId: 'g1', name: 'Alice', color: 'w' }));

  // Mallory cannot invite Bob while pretending to be Alice.
  await assertFails(
    set(ref(dbFor(MALLORY), `invites/${BOB}/${ALICE}`), { gameId: 'g9', name: 'Alice', color: 'w' }));
});

test('the invitee reads their inbox; nobody else reads it', async () => {
  await assertSucceeds(
    set(ref(dbFor(ALICE), `invites/${BOB}/${ALICE}`), { gameId: 'g1', name: 'Alice', color: 'w' }));

  await assertSucceeds(get(ref(dbFor(BOB), `invites/${BOB}`)));
  await assertFails(get(ref(dbFor(MALLORY), `invites/${BOB}`)));

  // The inviter can still read their OWN row -- that is how Declined gets back to them.
  await assertSucceeds(get(ref(dbFor(ALICE), `invites/${BOB}/${ALICE}`)));
});

test('the invitee may decline, and that is the ONLY thing they may change', async () => {
  await assertSucceeds(
    set(ref(dbFor(ALICE), `invites/${BOB}/${ALICE}`), { gameId: 'g1', name: 'Alice', color: 'w' }));

  await assertSucceeds(set(ref(dbFor(BOB), `invites/${BOB}/${ALICE}/declined`), true));

  // Not the game it points at, not the colour, and not the row itself.
  await assertFails(set(ref(dbFor(BOB), `invites/${BOB}/${ALICE}/gameId`), 'g2'));
  await assertFails(set(ref(dbFor(BOB), `invites/${BOB}/${ALICE}/color`), 'b'));
  await assertFails(set(ref(dbFor(BOB), `invites/${BOB}/${ALICE}`), null));
});

test('the inviter may cancel their own invite', async () => {
  await assertSucceeds(
    set(ref(dbFor(ALICE), `invites/${BOB}/${ALICE}`), { gameId: 'g1', name: 'Alice', color: 'w' }));

  await assertSucceeds(set(ref(dbFor(ALICE), `invites/${BOB}/${ALICE}`), null));
});

test('an invite is not a pastebin either', async () => {
  await assertFails(
    set(ref(dbFor(ALICE), `invites/${BOB}/${ALICE}`),
        { gameId: 'g1', name: 'Alice', color: 'w', payload: 'x'.repeat(1000) }));

  // And it must actually be an invite.
  await assertFails(set(ref(dbFor(ALICE), `invites/${BOB}/${ALICE}`), { name: 'Alice' }));
});

test('accepting an invite is claiming the seat it points at', async () => {
  // The whole point of shaping the cloud handshake like LAN's: Accept is not a new mechanism, it is
  // the seat claim the rules already gate. Alice posts a game with only her seat filled, invites Bob,
  // and Bob's Accept is a write to the empty seat.
  await env.withSecurityRulesDisabled(async (ctx) => {
    await set(ref(ctx.database(), 'games/g1'), { g: '', n: 0, w: { uid: ALICE, name: 'Alice' } });
  });
  await assertSucceeds(
    set(ref(dbFor(ALICE), `invites/${BOB}/${ALICE}`), { gameId: 'g1', name: 'Alice', color: 'w' }));

  // Bob cannot read the game before accepting -- he holds no seat yet.
  await assertFails(get(ref(dbFor(BOB), 'games/g1')));

  await assertSucceeds(set(ref(dbFor(BOB), 'games/g1/b'), { uid: BOB, name: 'Bob' }));
  await assertSucceeds(get(ref(dbFor(BOB), 'games/g1')));

  // A third party invited nowhere still cannot take the seat that is now taken.
  await assertFails(set(ref(dbFor(MALLORY), 'games/g1/b'), { uid: MALLORY, name: 'M' }));
});

// ── The lobby ─────────────────────────────────────────────────────

test('an open game is keyed by its host, which caps them at one each', async () => {
  const alice = dbFor(ALICE);

  await assertSucceeds(set(ref(alice, `open/${ALICE}`), { gameId: 'g1', name: 'Alice', color: 'w' }));
  // A second post overwrites the first rather than adding to it -- "one open game per identity" as
  // a property of the PATH, since rules cannot count.
  await assertSucceeds(set(ref(alice, `open/${ALICE}`), { gameId: 'g2', name: 'Alice', color: 'b' }));

  // withSecurityRulesDisabled does not propagate its callback's return value, so read out sideways.
  let posted;
  await env.withSecurityRulesDisabled(async (ctx) => {
    posted = (await get(ref(ctx.database(), 'open'))).val();
  });

  assert.equal(Object.keys(posted).length, 1);
  assert.equal(posted[ALICE].gameId, 'g2');
});

test('nobody can post an open game under another player\'s name', async () => {
  await assertFails(set(ref(dbFor(MALLORY), `open/${ALICE}`), { gameId: 'g1', name: 'A', color: 'w' }));
});

test('the lobby is readable by any signed-in player, since that is what a lobby is for', async () => {
  await assertSucceeds(set(ref(dbFor(ALICE), `open/${ALICE}`), { gameId: 'g1', name: 'Alice', color: 'w' }));
  await assertSucceeds(get(ref(dbFor(MALLORY), 'open')));
});
