using Inventory.Features.Posting;
using Inventory.Features.Posting.PostAdjustment;
using Inventory.Features.Posting.PostIssue;
using Inventory.Features.Posting.PostReceipt;
using Inventory.Features.Posting.PostTransfer;
using Inventory.Persistence;

using Microsoft.Extensions.Primitives;

using Shouldly;

namespace Inventory.UnitTests;

public sealed class PostingValidationTests
{
    private static readonly Guid Loc = Guid.NewGuid();
    private static readonly Guid Item = Guid.NewGuid();
    private static readonly Guid OtherItem = Guid.NewGuid();
    private static readonly Guid OtherLoc = Guid.NewGuid();

    private static PostReceiptLine ReceiptLine(decimal? quantity = 1m, decimal? unitCost = 10m) => new(Item, quantity, unitCost);

    private static PostReceiptRequest Receipt(params PostReceiptLine?[] lines) => new(Loc, null, null, lines);

    private static PostIssueRequest Issue(params PostIssueLine?[] lines) => new(Loc, null, null, lines);

    private static Dictionary<string, string[]> ReceiptErrors(PostReceiptRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        PostingValidation.Receipt(request, errors).ShouldBeNull();
        return errors;
    }

    private static Dictionary<string, string[]> IssueErrors(PostIssueRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        PostingValidation.Issue(request, errors).ShouldBeNull();
        return errors;
    }

    private static ValidatedPosting ValidReceipt(PostReceiptRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var posting = PostingValidation.Receipt(request, errors);
        errors.ShouldBeEmpty();
        return posting.ShouldNotBeNull();
    }

    private static ValidatedPosting ValidIssue(PostIssueRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var posting = PostingValidation.Issue(request, errors);
        errors.ShouldBeEmpty();
        return posting.ShouldNotBeNull();
    }

    private static PostTransferRequest Transfer(params PostTransferLine?[] lines) => new(Loc, OtherLoc, null, null, lines);

    private static PostAdjustmentRequest Adjustment(params PostAdjustmentLine?[] lines) => new(Loc, "Damaged", null, null, lines);

    private static Dictionary<string, string[]> TransferErrors(PostTransferRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        PostingValidation.Transfer(request, errors).ShouldBeNull();
        return errors;
    }

    private static Dictionary<string, string[]> AdjustmentErrors(PostAdjustmentRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        PostingValidation.Adjustment(request, errors).ShouldBeNull();
        return errors;
    }

    private static ValidatedPosting ValidTransfer(PostTransferRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var posting = PostingValidation.Transfer(request, errors);
        errors.ShouldBeEmpty();
        return posting.ShouldNotBeNull();
    }

    private static ValidatedPosting ValidAdjustment(PostAdjustmentRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var posting = PostingValidation.Adjustment(request, errors);
        errors.ShouldBeEmpty();
        return posting.ShouldNotBeNull();
    }

    private static PostReceiptLine[] DistinctReceiptLines(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => new PostReceiptLine(Guid.NewGuid(), 1m, 10m))];

    [Fact]
    public void A_valid_receipt_is_normalized_and_numbered_from_1()
    {
        var posting = ValidReceipt(new PostReceiptRequest(
            Loc,
            "  DR-1 ",
            "   ",
            [new PostReceiptLine(Item, 6m, 125m), new PostReceiptLine(OtherItem, 1.50000m, 0m)]));

        posting.LocationId.ShouldBe(Loc);
        posting.Reference.ShouldBe("DR-1");
        posting.Notes.ShouldBeNull();
        posting.Lines.ShouldBe(
        [
            new ValidatedLine(1, Item, 6m, 125m),
            new ValidatedLine(2, OtherItem, 1.5m, 0m),
        ]);
    }

    [Fact]
    public void A_valid_issue_is_normalized_and_has_no_unit_cost()
    {
        var posting = ValidIssue(new PostIssueRequest(
            Loc,
            "  DR-1 ",
            "   ",
            [new PostIssueLine(Item, 2m), new PostIssueLine(OtherItem, 3m)]));

        posting.Reference.ShouldBe("DR-1");
        posting.Notes.ShouldBeNull();
        posting.Lines.ShouldBe(
        [
            new ValidatedLine(1, Item, 2m, null),
            new ValidatedLine(2, OtherItem, 3m, null),
        ]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void LocationId_is_required(string? locationId)
    {
        var location = locationId is null ? (Guid?)null : Guid.Parse(locationId);

        ReceiptErrors(new PostReceiptRequest(location, null, null, [ReceiptLine()])).Keys.ShouldBe(["locationId"]);
        IssueErrors(new PostIssueRequest(location, null, null, [new PostIssueLine(Item, 1m)])).Keys.ShouldBe(["locationId"]);
    }

    [Fact]
    public void Reference_of_100_characters_after_trimming_passes()
    {
        var reference = new string('R', 100);

        ValidReceipt(new PostReceiptRequest(Loc, $"  {reference}  ", null, [ReceiptLine()])).Reference.ShouldBe(reference);
    }

    [Fact]
    public void Reference_over_100_characters_fails()
    {
        ReceiptErrors(new PostReceiptRequest(Loc, new string('R', 101), null, [ReceiptLine()])).Keys.ShouldBe(["reference"]);
    }

    [Fact]
    public void Notes_of_500_characters_after_trimming_passes()
    {
        var notes = new string('N', 500);

        ValidIssue(new PostIssueRequest(Loc, null, $" {notes} ", [new PostIssueLine(Item, 1m)])).Notes.ShouldBe(notes);
    }

    [Fact]
    public void Notes_over_500_characters_fails()
    {
        IssueErrors(new PostIssueRequest(Loc, null, new string('N', 501), [new PostIssueLine(Item, 1m)])).Keys.ShouldBe(["notes"]);
    }

    [Fact]
    public void Lines_are_required()
    {
        ReceiptErrors(new PostReceiptRequest(Loc, null, null, null)).Keys.ShouldBe(["lines"]);
        IssueErrors(new PostIssueRequest(Loc, null, null, null)).Keys.ShouldBe(["lines"]);
    }

    [Fact]
    public void Lines_must_not_be_empty()
    {
        ReceiptErrors(Receipt()).Keys.ShouldBe(["lines"]);
        IssueErrors(Issue()).Keys.ShouldBe(["lines"]);
    }

    [Fact]
    public void One_hundred_lines_pass()
    {
        var posting = ValidReceipt(Receipt(DistinctReceiptLines(100)));

        posting.Lines.Count.ShouldBe(100);
        posting.Lines[^1].LineNo.ShouldBe(100);
    }

    [Fact]
    public void One_hundred_and_one_lines_fail()
    {
        ReceiptErrors(Receipt(DistinctReceiptLines(101))).Keys.ShouldBe(["lines"]);
    }

    [Fact]
    public void A_null_line_fails_at_its_index()
    {
        ReceiptErrors(Receipt(ReceiptLine(), null)).Keys.ShouldBe(["lines[1]"]);
        IssueErrors(Issue(new PostIssueLine(Item, 1m), null)).Keys.ShouldBe(["lines[1]"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void ItemId_is_required(string? itemId)
    {
        var item = itemId is null ? (Guid?)null : Guid.Parse(itemId);

        ReceiptErrors(Receipt(new PostReceiptLine(item, 1m, 10m))).Keys.ShouldBe(["lines[0].itemId"]);
        IssueErrors(Issue(new PostIssueLine(item, 1m))).Keys.ShouldBe(["lines[0].itemId"]);
    }

    [Fact]
    public void An_item_already_on_an_earlier_line_fails_on_the_later_line()
    {
        var errors = ReceiptErrors(Receipt(
            new PostReceiptLine(Item, 1m, 10m),
            new PostReceiptLine(OtherItem, 1m, 10m),
            new PostReceiptLine(Item, 2m, 20m)));

        errors.Keys.ShouldBe(["lines[2].itemId"]);
        errors["lines[2].itemId"].ShouldBe(["This item is already on another line."]);
    }

    [Fact]
    public void An_issue_with_the_same_item_twice_fails_on_the_later_line()
    {
        IssueErrors(Issue(new PostIssueLine(Item, 1m), new PostIssueLine(Item, 2m))).Keys.ShouldBe(["lines[1].itemId"]);
    }

    public static TheoryData<decimal?> InvalidQuantities => new()
    {
        (decimal?)null,
        0m,
        -1m,
        PostingValidation.MaxAmount + 0.0001m,
        0.00001m,
        1.23456m,
    };

    [Theory]
    [MemberData(nameof(InvalidQuantities))]
    public void An_invalid_quantity_fails(decimal? quantity)
    {
        ReceiptErrors(Receipt(ReceiptLine(quantity: quantity))).Keys.ShouldBe(["lines[0].quantity"]);
        IssueErrors(Issue(new PostIssueLine(Item, quantity))).Keys.ShouldBe(["lines[0].quantity"]);
    }

    public static TheoryData<decimal> ValidQuantities => new()
    {
        0.0001m,
        1.50000m,
        10.00000m,
        PostingValidation.MaxAmount,
    };

    [Theory]
    [MemberData(nameof(ValidQuantities))]
    public void A_valid_quantity_passes(decimal quantity)
    {
        ValidReceipt(Receipt(ReceiptLine(quantity: quantity, unitCost: 1m))).Lines[0].Quantity.ShouldBe(quantity);
        ValidIssue(Issue(new PostIssueLine(Item, quantity))).Lines[0].Quantity.ShouldBe(quantity);
    }

    public static TheoryData<decimal?> InvalidUnitCosts => new()
    {
        (decimal?)null,
        -0.0001m,
        PostingValidation.MaxAmount + 0.0001m,
        0.00001m,
    };

    [Theory]
    [MemberData(nameof(InvalidUnitCosts))]
    public void An_invalid_unit_cost_fails(decimal? unitCost)
    {
        ReceiptErrors(Receipt(ReceiptLine(unitCost: unitCost))).Keys.ShouldBe(["lines[0].unitCost"]);
    }

    public static TheoryData<decimal> ValidUnitCosts => new()
    {
        0m,
        0.0001m,
        12.50000m,
        PostingValidation.MaxAmount,
    };

    [Theory]
    [MemberData(nameof(ValidUnitCosts))]
    public void A_valid_unit_cost_passes(decimal unitCost)
    {
        ValidReceipt(Receipt(ReceiptLine(unitCost: unitCost))).Lines[0].UnitCost.ShouldBe(unitCost);
    }

    [Fact]
    public void A_line_total_beyond_decimal_19_4_fails_on_the_unit_cost()
    {
        var errors = ReceiptErrors(Receipt(ReceiptLine(PostingValidation.MaxAmount, PostingValidation.MaxAmount)));

        errors.Keys.ShouldBe(["lines[0].unitCost"]);
        errors["lines[0].unitCost"].ShouldBe(["The line total is too large."]);
    }

    [Fact]
    public void A_line_total_just_beyond_decimal_19_4_fails()
    {
        // 1,000,001 × 999,999,999.9999 = 1,000,000,999,999,999.9 > 999,999,999,999,999.9999
        ReceiptErrors(Receipt(ReceiptLine(1_000_001m, PostingValidation.MaxAmount))).Keys.ShouldBe(["lines[0].unitCost"]);
    }

    [Fact]
    public void A_line_total_within_decimal_19_4_passes()
    {
        // 1,000,000 × 999,999,999.9999 = 999,999,999,999,900
        ValidReceipt(Receipt(ReceiptLine(1_000_000m, PostingValidation.MaxAmount))).Lines.Count.ShouldBe(1);
    }

    [Fact]
    public void The_line_total_is_not_checked_when_the_quantity_is_invalid()
    {
        ReceiptErrors(Receipt(ReceiptLine(PostingValidation.MaxAmount + 1m, PostingValidation.MaxAmount)))
            .Keys.ShouldBe(["lines[0].quantity"]);
    }

    [Fact]
    public void Every_error_is_collected()
    {
        var errors = ReceiptErrors(new PostReceiptRequest(
            null,
            new string('R', 101),
            new string('N', 501),
            [new PostReceiptLine(Guid.Empty, 0m, -1m), null, new PostReceiptLine(Item, 1m, 0.00001m)]));

        errors.Keys.ShouldBe(
            ["locationId", "reference", "notes", "lines[0].itemId", "lines[0].quantity", "lines[0].unitCost", "lines[1]", "lines[2].unitCost"],
            ignoreOrder: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_missing_idempotency_key_is_reported_as_missing(string? header)
    {
        var errors = new Dictionary<string, string[]>();

        PostingValidation.IdempotencyKey(header, errors, out var missing).ShouldBeNull();

        missing.ShouldBeTrue();
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void An_idempotency_key_of_64_printable_characters_is_returned_as_sent()
    {
        var header = "Ab-" + new string('x', 60) + "~";
        var errors = new Dictionary<string, string[]>();

        PostingValidation.IdempotencyKey(header, errors, out var missing).ShouldBe(header);

        header.Length.ShouldBe(64);
        missing.ShouldBeFalse();
        errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("x", 65)]
    [InlineData("abc def", 1)]
    [InlineData(" abc", 1)]
    [InlineData("caf\u00e9", 1)]
    [InlineData("abc\u007f", 1)]
    public void An_invalid_idempotency_key_fails_validation(string text, int repeat)
    {
        var header = string.Concat(Enumerable.Repeat(text, repeat));
        var errors = new Dictionary<string, string[]>();

        PostingValidation.IdempotencyKey(header, errors, out var missing).ShouldBeNull();

        missing.ShouldBeFalse();
        errors.Keys.ShouldBe(["Idempotency-Key"]);
    }

    [Fact]
    public void A_single_idempotency_key_value_is_returned_as_sent()
    {
        var errors = new Dictionary<string, string[]>();

        PostingValidation.IdempotencyKey(new StringValues("k1"), errors, out var missing).ShouldBe("k1");

        missing.ShouldBeFalse();
        errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("k1", "k2")]
    [InlineData("k1", "k1")]
    [InlineData("", "k1")]
    public void Repeated_idempotency_key_headers_fail_validation(string first, string second)
    {
        var errors = new Dictionary<string, string[]>();

        // Joined, the values would be one key ("k1,k2") that the client never sent.
        PostingValidation.IdempotencyKey(new StringValues([first, second]), errors, out var missing).ShouldBeNull();

        missing.ShouldBeFalse();
        errors.Keys.ShouldBe(["Idempotency-Key"]);
        errors["Idempotency-Key"].ShouldBe(["Send exactly one Idempotency-Key header."]);
    }

    [Fact]
    public void A_valid_transfer_is_normalized_and_numbered_from_1()
    {
        var posting = ValidTransfer(new PostTransferRequest(
            Loc,
            OtherLoc,
            "  TR-1 ",
            "   ",
            [new PostTransferLine(Item, 2m), new PostTransferLine(OtherItem, 1.50000m)]));

        posting.LocationId.ShouldBe(Loc);
        posting.ToLocationId.ShouldBe(OtherLoc);
        posting.ReasonCode.ShouldBeNull();
        posting.Reference.ShouldBe("TR-1");
        posting.Notes.ShouldBeNull();
        posting.Lines.ShouldBe(
        [
            new ValidatedLine(1, Item, 2m, null),
            new ValidatedLine(2, OtherItem, 1.5m, null),
        ]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void A_transfer_needs_a_source_location(string? locationId)
    {
        var location = locationId is null ? (Guid?)null : Guid.Parse(locationId);

        var errors = TransferErrors(new PostTransferRequest(location, OtherLoc, null, null, [new PostTransferLine(Item, 1m)]));

        errors.Keys.ShouldBe(["fromLocationId"]);
        errors["fromLocationId"].ShouldBe(["Choose the location to move stock from."]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void A_transfer_needs_a_destination_location(string? locationId)
    {
        var location = locationId is null ? (Guid?)null : Guid.Parse(locationId);

        var errors = TransferErrors(new PostTransferRequest(Loc, location, null, null, [new PostTransferLine(Item, 1m)]));

        errors.Keys.ShouldBe(["toLocationId"]);
        errors["toLocationId"].ShouldBe(["Choose the location to move stock to."]);
    }

    [Fact]
    public void A_transfer_to_its_own_location_fails_on_the_destination()
    {
        var errors = TransferErrors(new PostTransferRequest(Loc, Loc, null, null, [new PostTransferLine(Item, 1m)]));

        errors.Keys.ShouldBe(["toLocationId"]);
        errors["toLocationId"].ShouldBe(["Choose a different location to move stock to."]);
    }

    [Theory]
    [MemberData(nameof(InvalidQuantities))]
    public void An_invalid_transfer_quantity_fails(decimal? quantity)
    {
        TransferErrors(Transfer(new PostTransferLine(Item, quantity))).Keys.ShouldBe(["lines[0].quantity"]);
    }

    [Fact]
    public void A_transfer_with_the_same_item_twice_fails_on_the_later_line()
    {
        TransferErrors(Transfer(new PostTransferLine(Item, 1m), new PostTransferLine(Item, 2m))).Keys.ShouldBe(["lines[1].itemId"]);
    }

    [Fact]
    public void A_valid_adjustment_keeps_signed_quantities_and_the_cost_on_the_positive_line()
    {
        var posting = ValidAdjustment(new PostAdjustmentRequest(
            Loc,
            "Damaged",
            "  ADJ-1 ",
            " water damage ",
            [new PostAdjustmentLine(Item, -2m, null), new PostAdjustmentLine(OtherItem, 5.0000m, 42.50m)]));

        posting.LocationId.ShouldBe(Loc);
        posting.ReasonCode.ShouldBe(AdjustmentReason.Damaged);
        posting.ToLocationId.ShouldBeNull();
        posting.Reference.ShouldBe("ADJ-1");
        posting.Notes.ShouldBe("water damage");
        posting.Lines.ShouldBe(
        [
            new ValidatedLine(1, Item, -2m, null),
            new ValidatedLine(2, OtherItem, 5m, 42.5m),
        ]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void An_adjustment_needs_a_location(string? locationId)
    {
        var location = locationId is null ? (Guid?)null : Guid.Parse(locationId);

        var errors = AdjustmentErrors(new PostAdjustmentRequest(location, "Lost", null, null, [new PostAdjustmentLine(Item, -1m, null)]));

        errors.Keys.ShouldBe(["locationId"]);
        errors["locationId"].ShouldBe(["Location is required."]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("damaged")]
    [InlineData("DAMAGED")]
    [InlineData(" Damaged")]
    [InlineData("1")]
    [InlineData("Bogus")]
    public void An_invalid_reason_fails(string? reason)
    {
        AdjustmentErrors(new PostAdjustmentRequest(Loc, reason, null, null, [new PostAdjustmentLine(Item, -1m, null)]))
            .Keys.ShouldBe(["reasonCode"]);
    }

    [Fact]
    public void A_blank_reason_and_an_unknown_reason_have_their_own_messages()
    {
        AdjustmentErrors(new PostAdjustmentRequest(Loc, " ", null, null, [new PostAdjustmentLine(Item, -1m, null)]))["reasonCode"]
            .ShouldBe(["Choose a reason."]);
        AdjustmentErrors(new PostAdjustmentRequest(Loc, "damaged", null, null, [new PostAdjustmentLine(Item, -1m, null)]))["reasonCode"]
            .ShouldBe(["Choose one of: Damaged, Lost, Found, CountCorrection, Other."]);
    }

    [Theory]
    [InlineData("Damaged")]
    [InlineData("Lost")]
    [InlineData("Found")]
    [InlineData("CountCorrection")]
    [InlineData("Other")]
    public void Each_reason_name_passes(string reason)
    {
        ValidAdjustment(new PostAdjustmentRequest(Loc, reason, null, null, [new PostAdjustmentLine(Item, -1m, null)]))
            .ReasonCode.ShouldBe(Enum.Parse<AdjustmentReason>(reason));
    }

    public static TheoryData<decimal?> InvalidQuantityDeltas => new()
    {
        (decimal?)null,
        0m,
        PostingValidation.MaxAmount + 0.0001m,
        -(PostingValidation.MaxAmount + 0.0001m),
        0.00001m,
        -1.23456m,
    };

    [Theory]
    [MemberData(nameof(InvalidQuantityDeltas))]
    public void An_invalid_quantity_delta_fails(decimal? quantityDelta)
    {
        AdjustmentErrors(Adjustment(new PostAdjustmentLine(Item, quantityDelta, null))).Keys.ShouldBe(["lines[0].quantityDelta"]);
    }

    [Fact]
    public void An_out_of_range_quantity_delta_names_both_bounds()
    {
        AdjustmentErrors(Adjustment(new PostAdjustmentLine(Item, -(PostingValidation.MaxAmount + 0.0001m), null)))["lines[0].quantityDelta"]
            .ShouldBe(["Quantity change must be between -999,999,999.9999 and 999,999,999.9999."]);
    }

    public static TheoryData<decimal> ValidQuantityDeltas => new()
    {
        -PostingValidation.MaxAmount,
        -0.0001m,
        0.0001m,
        PostingValidation.MaxAmount,
    };

    [Theory]
    [MemberData(nameof(ValidQuantityDeltas))]
    public void A_valid_quantity_delta_passes_with_its_sign(decimal quantityDelta)
    {
        ValidAdjustment(Adjustment(new PostAdjustmentLine(Item, quantityDelta, null))).Lines[0].Quantity.ShouldBe(quantityDelta);
    }

    [Fact]
    public void A_line_that_removes_stock_must_not_carry_a_unit_cost()
    {
        var errors = AdjustmentErrors(Adjustment(new PostAdjustmentLine(Item, -1m, 0m)));

        errors.Keys.ShouldBe(["lines[0].unitCost"]);
        errors["lines[0].unitCost"].ShouldBe(["Leave the unit cost empty on a line that removes stock."]);
    }

    [Fact]
    public void A_line_that_adds_stock_without_a_unit_cost_passes()
    {
        ValidAdjustment(Adjustment(new PostAdjustmentLine(Item, 3m, null))).Lines[0].UnitCost.ShouldBeNull();
    }

    public static TheoryData<decimal> InvalidAdjustmentUnitCosts => new()
    {
        -0.0001m,
        PostingValidation.MaxAmount + 0.0001m,
        0.00001m,
    };

    [Theory]
    [MemberData(nameof(InvalidAdjustmentUnitCosts))]
    public void An_invalid_adjustment_unit_cost_fails(decimal unitCost)
    {
        AdjustmentErrors(Adjustment(new PostAdjustmentLine(Item, 1m, unitCost))).Keys.ShouldBe(["lines[0].unitCost"]);
    }

    [Fact]
    public void An_adjustment_line_total_beyond_decimal_19_4_fails_on_the_unit_cost()
    {
        var errors = AdjustmentErrors(Adjustment(new PostAdjustmentLine(Item, PostingValidation.MaxAmount, PostingValidation.MaxAmount)));

        errors.Keys.ShouldBe(["lines[0].unitCost"]);
        errors["lines[0].unitCost"].ShouldBe(["The line total is too large."]);
    }
}