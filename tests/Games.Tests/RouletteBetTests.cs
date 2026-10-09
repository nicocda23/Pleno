using Casino.Modules.Games.Roulette;

namespace Casino.Games.Tests;

public class RouletteBetTests
{
    private static RouletteBet Bet(RouletteBetType type, long stake, params int[] selection) =>
        RouletteBet.Create(type, selection, stake);

    private static bool IsValid(RouletteBetType type, params int[] selection) =>
        RouletteBet.TryCreate(type, selection, 10, out _);

    // --- Validacion exhaustiva de combinaciones legales

    [Fact]
    public void Exactly_60_pairs_are_legal_splits()
    {
        var legal = 0;
        for (var a = 0; a <= 36; a++)
        {
            for (var b = a + 1; b <= 36; b++)
            {
                if (IsValid(RouletteBetType.Split, a, b))
                {
                    legal++;
                }
            }
        }

        Assert.Equal(60, legal);
    }

    [Fact]
    public void Exactly_12_triples_are_streets_and_2_are_trios()
    {
        int streets = 0, trios = 0;
        for (var a = 0; a <= 36; a++)
        {
            for (var b = a + 1; b <= 36; b++)
            {
                for (var c = b + 1; c <= 36; c++)
                {
                    streets += IsValid(RouletteBetType.Street, a, b, c) ? 1 : 0;
                    trios += IsValid(RouletteBetType.Trio, a, b, c) ? 1 : 0;
                }
            }
        }

        Assert.Equal(12, streets);
        Assert.Equal(2, trios);
    }

    [Fact]
    public void Exactly_22_quads_are_corners_and_1_is_the_first_four()
    {
        int corners = 0, firstFour = 0;
        for (var a = 0; a <= 36; a++)
        {
            for (var b = a + 1; b <= 36; b++)
            {
                for (var c = b + 1; c <= 36; c++)
                {
                    for (var d = c + 1; d <= 36; d++)
                    {
                        corners += IsValid(RouletteBetType.Corner, a, b, c, d) ? 1 : 0;
                        firstFour += IsValid(RouletteBetType.FirstFour, a, b, c, d) ? 1 : 0;
                    }
                }
            }
        }

        Assert.Equal(22, corners);
        Assert.Equal(1, firstFour);
    }

    [Fact]
    public void Six_lines_are_two_consecutive_streets()
    {
        for (var first = 1; first <= 31; first += 3)
        {
            Assert.True(IsValid(RouletteBetType.SixLine, first, first + 1, first + 2, first + 3, first + 4, first + 5));
        }

        Assert.False(IsValid(RouletteBetType.SixLine, 2, 3, 4, 5, 6, 7));
        Assert.False(IsValid(RouletteBetType.SixLine, 34, 35, 36, 37, 38, 39));
        Assert.False(IsValid(RouletteBetType.SixLine, 1, 2, 3, 4, 5));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(36, true)]
    [InlineData(37, false)]
    [InlineData(-1, false)]
    public void Straight_bets_only_accept_real_pockets(int number, bool legal)
    {
        Assert.Equal(legal, IsValid(RouletteBetType.Straight, number));
    }

    [Fact]
    public void Malformed_selections_are_rejected()
    {
        Assert.False(IsValid(RouletteBetType.Straight));
        Assert.False(IsValid(RouletteBetType.Straight, 1, 2));
        Assert.False(IsValid(RouletteBetType.Split, 1, 1));
        Assert.False(IsValid(RouletteBetType.Split, 3, 4));   // 3 y 4 estan en filas distintas
        Assert.False(IsValid(RouletteBetType.Red, 1));
        Assert.False(IsValid(RouletteBetType.Dozen, 4));
        Assert.False(IsValid(RouletteBetType.Column, 0));
        Assert.False(IsValid(RouletteBetType.Trio, 0, 1, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Stake_must_be_positive(long stake)
    {
        Assert.False(RouletteBet.TryCreate(RouletteBetType.Red, [], stake, out _));
    }

    // --- Que numeros cubre cada apuesta

    [Theory]
    [InlineData(RouletteBetType.Red, 18)]
    [InlineData(RouletteBetType.Black, 18)]
    [InlineData(RouletteBetType.Even, 18)]
    [InlineData(RouletteBetType.Odd, 18)]
    [InlineData(RouletteBetType.Low, 18)]
    [InlineData(RouletteBetType.High, 18)]
    public void Outside_bets_cover_18_numbers_and_never_the_zero(RouletteBetType type, int count)
    {
        var bet = Bet(type, 10);

        Assert.Equal(count, bet.Covered.Count);
        Assert.DoesNotContain(0, bet.Covered);
    }

    [Theory]
    [InlineData(RouletteBetType.Dozen, 1, 1, 12)]
    [InlineData(RouletteBetType.Dozen, 2, 13, 24)]
    [InlineData(RouletteBetType.Dozen, 3, 25, 36)]
    public void Dozens_cover_twelve_consecutive_numbers(RouletteBetType type, int dozen, int first, int last)
    {
        var bet = Bet(type, 10, dozen);

        Assert.Equal(Enumerable.Range(first, 12), bet.Covered);
        Assert.Equal(last, bet.Covered[^1]);
    }

    [Fact]
    public void Columns_cover_every_third_number()
    {
        Assert.Equal(Enumerable.Range(0, 12).Select(i => 1 + (3 * i)), Bet(RouletteBetType.Column, 10, 1).Covered);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => 2 + (3 * i)), Bet(RouletteBetType.Column, 10, 2).Covered);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => 3 + (3 * i)), Bet(RouletteBetType.Column, 10, 3).Covered);
    }

    [Fact]
    public void Red_numbers_follow_the_european_wheel()
    {
        var red = Bet(RouletteBetType.Red, 10);

        Assert.True(red.Wins(1));
        Assert.True(red.Wins(36));
        Assert.False(red.Wins(2));
        Assert.False(red.Wins(0));
        Assert.Equal(Enumerable.Range(1, 36), red.Covered.Concat(Bet(RouletteBetType.Black, 10).Covered).Order());
    }

    // --- Pagos

    [Theory]
    [InlineData(RouletteBetType.Straight, new[] { 17 }, 17, 360)]
    [InlineData(RouletteBetType.Split, new[] { 17, 18 }, 18, 180)]
    [InlineData(RouletteBetType.Street, new[] { 7, 8, 9 }, 8, 120)]
    [InlineData(RouletteBetType.Trio, new[] { 0, 1, 2 }, 0, 120)]
    [InlineData(RouletteBetType.Corner, new[] { 8, 9, 11, 12 }, 11, 90)]
    [InlineData(RouletteBetType.FirstFour, new[] { 0, 1, 2, 3 }, 3, 90)]
    [InlineData(RouletteBetType.SixLine, new[] { 7, 8, 9, 10, 11, 12 }, 12, 60)]
    [InlineData(RouletteBetType.Dozen, new[] { 2 }, 20, 30)]
    [InlineData(RouletteBetType.Column, new[] { 1 }, 34, 30)]
    [InlineData(RouletteBetType.Red, new int[0], 19, 20)]
    [InlineData(RouletteBetType.Low, new int[0], 18, 20)]
    public void Winning_bets_pay_the_total_including_the_stake(RouletteBetType type, int[] selection, int winning, long expected)
    {
        Assert.Equal(expected, Bet(type, 10, selection).PayoutFor(winning));
    }

    [Fact]
    public void Losing_bets_pay_nothing_and_zero_beats_outside_bets()
    {
        Assert.Equal(0, Bet(RouletteBetType.Straight, 10, 17).PayoutFor(18));
        Assert.Equal(0, Bet(RouletteBetType.Red, 10).PayoutFor(0));
        Assert.Equal(0, Bet(RouletteBetType.Even, 10).PayoutFor(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(1_000_003)]
    public void Payouts_are_always_whole_chips(long stake)
    {
        foreach (var bet in AllLegalBets(stake))
        {
            foreach (var number in bet.Covered)
            {
                Assert.Equal(0, bet.PayoutFor(number) % stake);
                Assert.Equal(stake * 36 / bet.Covered.Count, bet.PayoutFor(number));
            }
        }
    }

    [Fact]
    public void Every_legal_bet_returns_exactly_36_37ths_in_the_long_run()
    {
        // Suma de pagos sobre los 37 resultados equiprobables = 36 x apuesta, para TODA apuesta legal:
        // la ventaja de la casa es siempre 1/37 (2,7 %), sin excepciones ni apuestas "trampa".
        const long stake = 37;
        var bets = AllLegalBets(stake).ToList();

        Assert.Equal(60 + 37 + 12 + 2 + 22 + 1 + 11 + 3 + 3 + 6, bets.Count);
        foreach (var bet in bets)
        {
            var total = Enumerable.Range(0, RouletteBet.PocketCount).Sum(n => bet.PayoutFor(n));
            Assert.Equal(stake * 36, total);
        }
    }

    private static IEnumerable<RouletteBet> AllLegalBets(long stake)
    {
        for (var n = 0; n <= 36; n++)
        {
            yield return Bet(RouletteBetType.Straight, stake, n);
        }

        for (var a = 0; a <= 36; a++)
        {
            for (var b = a + 1; b <= 36; b++)
            {
                if (RouletteBet.TryCreate(RouletteBetType.Split, [a, b], stake, out var split))
                {
                    yield return split!;
                }
            }
        }

        for (var row = 0; row < 12; row++)
        {
            yield return Bet(RouletteBetType.Street, stake, (3 * row) + 1, (3 * row) + 2, (3 * row) + 3);
        }

        yield return Bet(RouletteBetType.Trio, stake, 0, 1, 2);
        yield return Bet(RouletteBetType.Trio, stake, 0, 2, 3);
        yield return Bet(RouletteBetType.FirstFour, stake, 0, 1, 2, 3);

        for (var n = 1; n <= 32; n++)
        {
            if (RouletteBet.TryCreate(RouletteBetType.Corner, [n, n + 1, n + 3, n + 4], stake, out var corner))
            {
                yield return corner!;
            }
        }

        for (var first = 1; first <= 31; first += 3)
        {
            yield return Bet(RouletteBetType.SixLine, stake, first, first + 1, first + 2, first + 3, first + 4, first + 5);
        }

        for (var i = 1; i <= 3; i++)
        {
            yield return Bet(RouletteBetType.Dozen, stake, i);
            yield return Bet(RouletteBetType.Column, stake, i);
        }

        foreach (var type in new[]
        {
            RouletteBetType.Red, RouletteBetType.Black, RouletteBetType.Even,
            RouletteBetType.Odd, RouletteBetType.Low, RouletteBetType.High,
        })
        {
            yield return Bet(type, stake);
        }
    }
}
