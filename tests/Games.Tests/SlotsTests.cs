using Casino.Modules.Games.Fairness;
using Casino.Modules.Games.Roulette;
using Casino.Modules.Games.Slots;
using Microsoft.Extensions.Configuration;

namespace Casino.Games.Tests;

public class SlotsPaytableTests
{
    private static SlotSymbol[] Two(long pay) => [new("A", 1, pay), new("B", 1, pay)];

    [Fact]
    public void The_default_table_returns_about_96_percent_and_pays_one_spin_in_three()
    {
        var table = SlotsPaytable.Default;

        Assert.Equal(64, table.TotalWeight);
        Assert.InRange(table.ReturnToPlayerPercent, 96.0, 96.3);
        Assert.InRange(table.HitRatePercent, 33.0, 34.5);
    }

    [Fact]
    public void The_return_is_computed_exactly_with_integers()
    {
        // Dos simbolos de igual peso: 8 combinaciones. AAA y BBB pagan x4 cada una: (1 + 1) * 4 = 8 de 8 -> 100%.
        var table = new SlotsPaytable(Two(4), [], maxStake: 100);

        Assert.Equal(8, table.Denominator);
        Assert.Equal(8, table.ExpectedPayoutUnits);
        Assert.Equal(100.0, table.ReturnToPlayerPercent);
    }

    [Fact]
    public void A_table_that_would_make_the_house_lose_is_refused()
    {
        var ex = Assert.Throws<ArgumentException>(() => new SlotsPaytable(Two(5), [], maxStake: 100));

        Assert.Contains("100%", ex.Message);
    }

    [Fact]
    public void A_table_that_never_pays_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new SlotsPaytable(Two(0), [], maxStake: 100));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    public void Weights_and_symbol_count_are_validated(int weight, int count)
    {
        var symbols = Enumerable.Range(0, count).Select(i => new SlotSymbol($"S{i}", weight, 1));

        Assert.Throws<ArgumentException>(() => new SlotsPaytable(symbols, [], maxStake: 100));
    }

    [Fact]
    public void Repeated_or_blank_symbol_names_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new SlotsPaytable([new("A", 1, 1), new("a", 1, 1)], [], 100));
        Assert.Throws<ArgumentException>(() => new SlotsPaytable([new("A", 1, 1), new(" ", 1, 1)], [], 100));
    }

    [Fact]
    public void Leading_pays_must_refer_to_a_known_symbol_and_a_partial_run()
    {
        SlotSymbol[] symbols = [new("A", 3, 2), new("B", 1, 2)];

        Assert.Throws<ArgumentException>(() => new SlotsPaytable(symbols, [new LeadingPay("Z", 1, 1)], 100));
        Assert.Throws<ArgumentException>(() => new SlotsPaytable(symbols, [new LeadingPay("A", 3, 1)], 100)); // 3 es el pago triple
        Assert.Throws<ArgumentException>(() => new SlotsPaytable(symbols, [new LeadingPay("A", 1, 1), new LeadingPay("a", 1, 2)], 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_001)]
    public void The_maximum_stake_is_bounded(long max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlotsPaytable(Two(2), [], max));
    }

    [Fact]
    public void Three_of_a_kind_and_leading_cherries_pay_according_to_the_table()
    {
        var t = SlotsPaytable.Default;
        int Cherry() => t.IndexOf("Cereza");
        int Seven() => t.IndexOf("Siete");
        int Lemon() => t.IndexOf("Limon");

        Assert.Equal(100, t.MultiplierFor([Seven(), Seven(), Seven()]));
        Assert.Equal(7, t.MultiplierFor([Cherry(), Cherry(), Cherry()]));
        Assert.Equal(3, t.MultiplierFor([Cherry(), Cherry(), Lemon()]));
        Assert.Equal(1, t.MultiplierFor([Cherry(), Lemon(), Lemon()]));
        Assert.Equal(1, t.MultiplierFor([Cherry(), Lemon(), Cherry()])); // la cereza del medio o del final no cuenta: solo la racha desde la izquierda
        Assert.Equal(0, t.MultiplierFor([Lemon(), Cherry(), Cherry()]));
        Assert.Equal(0, t.MultiplierFor([Seven(), Seven(), Lemon()]));
    }

    [Fact]
    public void Every_stop_of_the_reel_maps_to_a_symbol_in_proportion_to_its_weight()
    {
        var t = SlotsPaytable.Default;

        var counts = Enumerable.Range(0, t.TotalWeight).GroupBy(t.SymbolAtStop).ToDictionary(g => g.Key, g => g.Count());

        for (var i = 0; i < t.Symbols.Count; i++)
        {
            Assert.Equal(t.Symbols[i].Weight, counts[i]);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => t.SymbolAtStop(t.TotalWeight));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.SymbolAtStop(-1));
    }

    [Fact]
    public void A_malformed_combination_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SlotsPaytable.Default.MultiplierFor([0, 0]));
        Assert.Throws<ArgumentException>(() => SlotsPaytable.Default.MultiplierFor([0, 0, 99]));
    }

    [Fact]
    public void Without_a_Slots_section_the_default_table_is_used()
    {
        var config = new ConfigurationBuilder().Build();

        Assert.Same(SlotsPaytable.Default, SlotsOptions.Load(config));
    }

    [Fact]
    public void The_table_can_be_changed_from_configuration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Slots:MaxStake"] = "500",
            ["Slots:Symbols:0:Name"] = "Rojo",
            ["Slots:Symbols:0:Weight"] = "3",
            ["Slots:Symbols:0:TriplePayout"] = "1",
            ["Slots:Symbols:1:Name"] = "Azul",
            ["Slots:Symbols:1:Weight"] = "1",
            ["Slots:Symbols:1:TriplePayout"] = "3",
        }).Build();

        var table = SlotsOptions.Load(config);

        Assert.Equal(500, table.MaxStake);
        Assert.Equal(["Rojo", "Azul"], table.Symbols.Select(s => s.Name));
        // (27 * 1 + 1 * 3) / 64 = 30 / 64
        Assert.Equal(30, table.ExpectedPayoutUnits);
        Assert.Equal(64, table.Denominator);
    }

    [Fact]
    public void A_configuration_with_more_than_100_percent_return_does_not_start()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Slots:Symbols:0:Name"] = "A",
            ["Slots:Symbols:0:Weight"] = "1",
            ["Slots:Symbols:0:TriplePayout"] = "50",
            ["Slots:Symbols:1:Name"] = "B",
            ["Slots:Symbols:1:Weight"] = "1",
            ["Slots:Symbols:1:TriplePayout"] = "50",
        }).Build();

        Assert.Throws<ArgumentException>(() => SlotsOptions.Load(config));
    }
}

public class SlotsGameTests
{
    private const string ServerSeed = "9f2c4a7e1b3d58606a1f0e9d8c7b6a5f4e3d2c1b0a99887766554433221100ff";
    private const string ClientSeed = "pleno-lab";
    private static readonly SlotsPaytable Table = SlotsPaytable.Default;

    [Fact]
    public void A_spin_uses_one_number_of_the_fair_stream_per_reel()
    {
        var rng = new FairRng(ServerSeed, ClientSeed, 7);
        var expected = Enumerable.Range(0, 3).Select(_ => Table.Symbols[Table.SymbolAtStop(rng.NextInt(Table.TotalWeight))].Name).ToArray();

        var outcome = SlotsGame.Play(Table, 100, ServerSeed, ClientSeed, 7);

        Assert.Equal(expected, outcome.Reels);
    }

    [Fact]
    public void The_same_inputs_always_give_the_same_spin_and_each_nonce_its_own()
    {
        Assert.Equivalent(SlotsGame.Play(Table, 100, ServerSeed, ClientSeed, 3), SlotsGame.Play(Table, 100, ServerSeed, ClientSeed, 3));

        var different = Enumerable.Range(0, 30).Select(n => string.Join(',', SlotsGame.Play(Table, 100, ServerSeed, ClientSeed, n).Reels)).Distinct().Count();
        Assert.True(different > 5);
    }

    [Fact]
    public void The_payout_is_the_stake_times_the_multiplier_and_always_a_whole_number()
    {
        for (var nonce = 0; nonce < 2_000; nonce++)
        {
            var outcome = SlotsGame.Play(Table, 37, ServerSeed, ClientSeed, nonce);

            Assert.Equal(37 * outcome.Multiplier, outcome.Payout);
            Assert.InRange(outcome.Payout, 0, 37 * 100);
        }
    }

    [Fact]
    public void A_stake_of_zero_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SlotsGame.Play(Table, 0, ServerSeed, ClientSeed, 0));
    }

    [Fact]
    public void Over_many_spins_the_real_return_matches_the_table_and_wins_come_every_now_and_then()
    {
        const int spins = 200_000;
        long staked = 0;
        long paid = 0;
        var wins = 0;
        for (var nonce = 0; nonce < spins; nonce++)
        {
            var outcome = SlotsGame.Play(Table, 10, ServerSeed, ClientSeed, nonce);
            staked += 10;
            paid += outcome.Payout;
            if (outcome.Payout > 0)
            {
                wins++;
            }
        }

        var realReturn = 100.0 * paid / staked;
        var realHitRate = 100.0 * wins / spins;
        Assert.InRange(realReturn, Table.ReturnToPlayerPercent - 4, Table.ReturnToPlayerPercent + 4);
        Assert.InRange(realHitRate, Table.HitRatePercent - 1, Table.HitRatePercent + 1);
        Assert.True(paid < staked * 1.04, "la casa no deberia perder a la larga");
    }
}

public class SlotsSpinTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static SlotsSpin Placed() => new() { Id = Guid.NewGuid(), Status = RoundStatus.Placed, Stake = 100 };

    [Fact]
    public void A_spin_goes_placed_resolved_settled_and_repeating_a_step_changes_nothing()
    {
        var spin = Placed();

        Assert.True(spin.MarkResolved(["Cereza", "Limon", "Bar"], 1, 100));
        Assert.False(spin.MarkResolved(["Siete", "Siete", "Siete"], 100, 10_000)); // entrega duplicada
        Assert.Equal(100, spin.Payout);
        Assert.True(spin.MarkSettled(Now));
        Assert.False(spin.MarkSettled(Now));
        Assert.True(spin.IsClosed);
    }

    [Fact]
    public void A_rejected_spin_never_played_and_cannot_be_settled()
    {
        var spin = Placed();

        Assert.True(spin.MarkRejected("InsufficientFunds", Now));
        Assert.False(spin.MarkResolved(["A", "B", "C"], 0, 0));
        Assert.False(spin.MarkSettled(Now));
        Assert.Equal(RoundStatus.Rejected, spin.Status);
    }

    [Fact]
    public void A_voided_spin_keeps_the_drawn_result_but_cannot_be_settled_afterwards()
    {
        var spin = Placed();
        spin.MarkResolved(["Siete", "Siete", "Siete"], 100, 10_000);

        Assert.True(spin.MarkVoided("ReservationExpired", Now));
        Assert.False(spin.MarkSettled(Now));
        Assert.Equal(3, spin.Reels.Count);
        Assert.Equal(RoundStatus.Voided, spin.Status);
    }
}
