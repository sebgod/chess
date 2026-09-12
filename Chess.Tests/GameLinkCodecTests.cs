using Chess.Lib;
using Chess.UCI;
using Shouldly;
using Xunit;
using static Chess.Lib.Action;
using static Chess.Lib.Position;

using Action = Chess.Lib.Action;
using File = Chess.Lib.File;

namespace Chess.Tests;

public class GameLinkCodecTests
{
    private static Game PlayMoves(params Action[] actions)
    {
        var game = new Game();
        foreach (var action in actions)
        {
            game.TryMove(action).IsMoveOrCapture().ShouldBeTrue($"setup move {action} must be legal");
        }
        return game;
    }

    // ── Encode ─────────────────────────────────────────────────────

    [Fact]
    public void EncodeFragment_FreshGame_ReturnsEmptyMoveList()
    {
        GameLinkCodec.EncodeFragment(new Game()).ShouldBe("#g=");
    }

    [Fact]
    public void EncodeFragment_AfterMoves_JoinsWithDots()
    {
        var game = PlayMoves(DoMove(E2, E4), DoMove(E7, E5), DoMove(G1, F3));

        GameLinkCodec.EncodeFragment(game).ShouldBe("#g=e2e4.e7e5.g1f3");
    }

    [Fact]
    public void EncodeFragment_Promotion_IncludesPromotionLetter()
    {
        // March the a-pawn to promotion; RecordedPly.Action drops Promoted, so this guards the
        // codec's explicit Promote() reconstruction.
        var game = PlayMoves(
            DoMove(A2, A4), DoMove(B7, B5),
            DoMove(A4, B5), DoMove(B8, C6),
            DoMove(B5, B6), DoMove(H7, H6),
            DoMove(B6, B7), DoMove(H6, H5),
            Promote(B7, A8, PieceType.Queen));

        GameLinkCodec.EncodeFragment(game).ShouldEndWith(".b7a8q");
    }

    // ── Decode: happy paths ────────────────────────────────────────

    [Fact]
    public void TryDecode_EmptyMoveList_ReturnsFreshGameWhiteToMove()
    {
        var result = GameLinkCodec.TryDecode("#g=", out var game, out var error);

        result.ShouldBe(GameLinkResult.Ok);
        error.ShouldBeNull();
        game.ShouldNotBeNull();
        game.PlyCount.ShouldBe(0);
        game.CurrentSide.ShouldBe(Side.White);
    }

    [Theory]
    [InlineData("#g=e2e4")]
    [InlineData("g=e2e4")] // leading '#' optional
    [InlineData("#g=e2e4.e7e5.g1f3.b8c6")]
    public void TryDecode_RoundTrip_ReplaysIdenticalGame(string fragment)
    {
        var result = GameLinkCodec.TryDecode(fragment, out var game, out _);

        result.ShouldBe(GameLinkResult.Ok);
        var canonical = fragment.StartsWith('#') ? fragment : "#" + fragment;
        GameLinkCodec.EncodeFragment(game!).ShouldBe(canonical);
    }

    [Fact]
    public void TryDecode_TurnParity_DerivesSideToMove()
    {
        GameLinkCodec.TryDecode("#g=e2e4", out var afterOne, out _).ShouldBe(GameLinkResult.Ok);
        afterOne!.CurrentSide.ShouldBe(Side.Black);

        GameLinkCodec.TryDecode("#g=e2e4.e7e5", out var afterTwo, out _).ShouldBe(GameLinkResult.Ok);
        afterTwo!.CurrentSide.ShouldBe(Side.White);
    }

    [Fact]
    public void TryDecode_CastlingRights_SurviveReplay()
    {
        // The design rationale: rights derive from ply history, so a replayed link must still
        // allow castling. Clear the kingside, castle, and expect the O-O to replay legally.
        var result = GameLinkCodec.TryDecode("#g=e2e4.e7e5.g1f3.b8c6.f1c4.g8f6.e1g1", out var game, out var error);

        result.ShouldBe(GameLinkResult.Ok, error);
        game!.PlyCount.ShouldBe(7);
        game.Board[G1].ShouldBe(new Piece(PieceType.King, Side.White));
        game.Board[F1].ShouldBe(new Piece(PieceType.Rook, Side.White));
    }

    [Fact]
    public void TryDecode_EnPassant_ReplaysCorrectly()
    {
        // e5xd6 e.p. — only legal because d7d5 is the immediately preceding ply.
        var result = GameLinkCodec.TryDecode("#g=e2e4.h7h6.e4e5.d7d5.e5d6", out var game, out var error);

        result.ShouldBe(GameLinkResult.Ok, error);
        game!.Board[D6].ShouldBe(new Piece(PieceType.Pawn, Side.White));
        game.Board[D5].PieceType.ShouldBe(PieceType.None); // the captured pawn is gone
    }

    [Fact]
    public void TryDecode_Promotion_RoundTrips()
    {
        var game = PlayMoves(
            DoMove(A2, A4), DoMove(B7, B5),
            DoMove(A4, B5), DoMove(B8, C6),
            DoMove(B5, B6), DoMove(H7, H6),
            DoMove(B6, B7), DoMove(H6, H5),
            Promote(B7, A8, PieceType.Queen));
        var fragment = GameLinkCodec.EncodeFragment(game);

        var result = GameLinkCodec.TryDecode(fragment, out var decoded, out var error);

        result.ShouldBe(GameLinkResult.Ok, error);
        decoded!.Board[A8].ShouldBe(new Piece(PieceType.Queen, Side.White));
    }

    [Fact]
    public void TryDecode_UnknownKey_IsIgnored()
    {
        var result = GameLinkCodec.TryDecode("#x=1&g=e2e4&y=2", out var game, out _);

        result.ShouldBe(GameLinkResult.Ok);
        game!.PlyCount.ShouldBe(1);
    }

    // ── Decode: NoLink (silent fallback) ───────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#unrelated")]
    [InlineData("#x=1&y=2")]
    public void TryDecode_NoGameKey_ReturnsNoLink(string fragment)
    {
        GameLinkCodec.TryDecode(fragment, out var game, out var error).ShouldBe(GameLinkResult.NoLink);
        game.ShouldBeNull();
        error.ShouldBeNull();
    }

    // ── Decode: Invalid (error surfaced) ───────────────────────────

    [Theory]
    [InlineData("#g=e2e9")]  // rank out of range
    [InlineData("#g=zz11")]  // garbage token
    [InlineData("#g=e2")]    // too short
    [InlineData("#g=e2e4x")] // bad promotion letter
    public void TryDecode_MalformedUciToken_ReturnsInvalid(string fragment)
    {
        GameLinkCodec.TryDecode(fragment, out var game, out var error).ShouldBe(GameLinkResult.Invalid);
        game.ShouldBeNull();
        error.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("#g=e2e5")]      // pawn can't jump three
    [InlineData("#g=e2e4.e2e4")] // White moving twice
    [InlineData("#g=e7e5")]      // Black piece on White's turn
    public void TryDecode_IllegalMoveSequence_ReturnsInvalid(string fragment)
    {
        GameLinkCodec.TryDecode(fragment, out var game, out var error).ShouldBe(GameLinkResult.Invalid);
        game.ShouldBeNull();
        error.ShouldNotBeNull();
    }

    [Fact]
    public void TryDecode_PlacementParam_ReturnsInvalid()
    {
        // The reserved future custom-start param must be rejected, never silently ignored — an
        // old build must not replay a custom-start link from the standard start.
        var result = GameLinkCodec.TryDecode("#f=8/8/8/8/8/8/8/QK6&g=", out var game, out var error);

        result.ShouldBe(GameLinkResult.Invalid);
        game.ShouldBeNull();
        error.ShouldNotBeNull();
        error.ShouldContain("supported", Case.Insensitive);
    }

    [Fact]
    public void TryDecode_TooManyMoves_ReturnsInvalidWithoutReplaying()
    {
        var tokens = string.Join('.', Enumerable.Repeat("e2e4", GameLinkCodec.MaxPlies + 1));

        var result = GameLinkCodec.TryDecode($"#g={tokens}", out var game, out var error);

        result.ShouldBe(GameLinkResult.Invalid);
        game.ShouldBeNull();
        error.ShouldNotBeNull();
        error.ShouldContain("too many");
    }

    // ── Payload reduction (ExtractBody) ────────────────────────

    [Theory]
    [InlineData("https://sebgod.github.io/chess/#g=e2e4.e7e5")] // shared page URL
    [InlineData("chess://play?g=e2e4.e7e5")]                    // custom scheme
    [InlineData("#g=e2e4.e7e5")]                                // bare fragment
    [InlineData("g=e2e4.e7e5")]                                 // bare body
    public void ExtractBody_EveryShapeAGameCanArriveIn_ReducesToTheSameBody(string received)
    {
        GameLinkCodec.ExtractBody(received).ShouldBe("g=e2e4.e7e5");
    }

    [Theory]
    [InlineData("https://sebgod.github.io/chess/#g=e2e4.e7e5")]
    [InlineData("chess://play?g=e2e4.e7e5")]
    [InlineData("#g=e2e4.e7e5")]
    [InlineData("g=e2e4.e7e5")]
    public void TryDecode_AcceptsEveryShapeDirectly_AndReplaysTheSameGame(string received)
    {
        // The point of the reduction living inside TryDecode: a host hands over whatever it was
        // given — argv, a clipboard paste, a URL scheme payload — and never writes a parser.
        var result = GameLinkCodec.TryDecode(received, out var game, out var error);

        result.ShouldBe(GameLinkResult.Ok, error);
        game.ShouldNotBeNull();
        game.PlyCount.ShouldBe(2);
        GameLinkCodec.EncodeFragment(game).ShouldBe("#g=e2e4.e7e5");
    }

    [Fact]
    public void ExtractBody_FragmentWinsOverQuery()
    {
        // Standard URL semantics: the game lives in the fragment, so a link carrying both is a
        // link to what its fragment says — never a merge of the two.
        GameLinkCodec.ExtractBody("chess://play?g=e2e4#g=d2d4").ShouldBe("g=d2d4");
    }

    [Theory]
    [InlineData("  #g=e2e4  ")]
    [InlineData("\n#g=e2e4\n")]
    [InlineData("\t https://sebgod.github.io/chess/#g=e2e4 \r\n")]
    public void ExtractBody_TrimsSurroundingWhitespace(string received)
    {
        // A link pasted out of a chat client or handed over on a command line routinely carries a
        // trailing newline; without the trim the last move token would fail to parse.
        GameLinkCodec.ExtractBody(received).ShouldBe("g=e2e4");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractBody_NothingReceived_ReturnsEmptyWhichDecodesAsNoLink(string? received)
    {
        // A bare launch (no argv, empty clipboard) must reach the wizard, not an error.
        GameLinkCodec.ExtractBody(received).ShouldBe("");
        GameLinkCodec.TryDecode(GameLinkCodec.ExtractBody(received), out _, out _)
            .ShouldBe(GameLinkResult.NoLink);
    }

    [Fact]
    public void TryDecode_UrlWithNoGameKey_IsNoLinkNotInvalid()
    {
        // Launching with some unrelated argument must fall through to the normal startup flow
        // silently — it is not a malformed link, it is not a link.
        GameLinkCodec.TryDecode("https://sebgod.github.io/chess/", out var game, out var error)
            .ShouldBe(GameLinkResult.NoLink);

        game.ShouldBeNull();
        error.ShouldBeNull();
    }

    // ── Continuation vs a different game ────────────────────

    [Fact]
    public void IsContinuationOf_TheOpponentsReply_IsAContinuation()
    {
        var mine = PlayMoves(DoMove(E2, E4));
        var theirReply = PlayMoves(DoMove(E2, E4), DoMove(E7, E5));

        GameLinkCodec.IsContinuationOf(mine, theirReply).ShouldBeTrue();
    }

    [Fact]
    public void IsContinuationOf_TheSameGame_IsAContinuation()
    {
        // Re-pasting the link you already have must not read as "a different game" and cost
        // somebody their board.
        var game = PlayMoves(DoMove(E2, E4), DoMove(E7, E5));

        GameLinkCodec.IsContinuationOf(game, PlayMoves(DoMove(E2, E4), DoMove(E7, E5))).ShouldBeTrue();
    }

    [Fact]
    public void IsContinuationOf_AFreshGame_ContinuesIntoAnything()
    {
        GameLinkCodec.IsContinuationOf(new Game(), PlayMoves(DoMove(D2, D4))).ShouldBeTrue();
    }

    [Fact]
    public void IsContinuationOf_ADivergentLine_IsNot()
    {
        var mine = PlayMoves(DoMove(E2, E4), DoMove(E7, E5));
        var other = PlayMoves(DoMove(E2, E4), DoMove(C7, C5));

        GameLinkCodec.IsContinuationOf(mine, other).ShouldBeFalse();
    }

    [Fact]
    public void IsContinuationOf_AShorterGame_IsNot()
    {
        // A rolled-back history is not a reply. (Server-side the cloud courier refuses this with an
        // append-only rule; this is the same policy, client side.)
        var mine = PlayMoves(DoMove(E2, E4), DoMove(E7, E5));

        GameLinkCodec.IsContinuationOf(mine, PlayMoves(DoMove(E2, E4))).ShouldBeFalse();
    }

    [Fact]
    public void IsContinuationOf_HandlesTheVariableLengthPromotionToken()
    {
        // Promotions are where a naive string-prefix test comes closest to breaking, because a
        // promotion token is 5 characters where every other move is 4 ("e7e8" is a string prefix of
        // "e7e8q"). Chess itself keeps that from becoming a wrong answer here — from one position the
        // piece on e7 is either a pawn that must promote or something that cannot — so this is a
        // regression guard, not a demonstration of a live bug.
        //
        // The reason to compare decoded plies anyway is stronger than any single case: a string test
        // compares an ENCODING, and the encoding carries things that are not the game (the '&'
        // key=value params reserved for forward compatibility), so it can be made to answer wrongly
        // by a change that has nothing to do with the moves.
        var pushed = PlayMoves(
            DoMove(A2, A4), DoMove(B7, B5),
            DoMove(A4, B5), DoMove(B8, C6),
            DoMove(B5, B6), DoMove(H7, H6),
            DoMove(B6, B7), DoMove(H6, H5));

        var promoted = PlayMoves(
            DoMove(A2, A4), DoMove(B7, B5),
            DoMove(A4, B5), DoMove(B8, C6),
            DoMove(B5, B6), DoMove(H7, H6),
            DoMove(B6, B7), DoMove(H6, H5),
            Promote(B7, A8, PieceType.Queen));

        // Here both tests agree, and should: the promotion genuinely does extend that line.
        GameLinkCodec.IsContinuationOf(pushed, promoted).ShouldBeTrue();

        // The trap bites the other way round: a bare push is NOT a continuation of the promotion.
        GameLinkCodec.IsContinuationOf(promoted, pushed).ShouldBeFalse();
    }
}
