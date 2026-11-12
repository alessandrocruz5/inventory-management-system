using System.Security.Cryptography;
using System.Text;

using Inventory.Features.Posting;
using Inventory.Features.Posting.PostAdjustment;
using Inventory.Features.Posting.PostIssue;
using Inventory.Features.Posting.PostReceipt;
using Inventory.Features.Posting.PostTransfer;

using Shouldly;

namespace Inventory.UnitTests;

public sealed class RequestHashTests
{
    private static readonly Guid Loc = Guid.Parse("0F8FAD5B-D9CB-469F-A165-70867728950E");
    private static readonly Guid OtherLoc = Guid.Parse("3B241101-E2BB-4255-8CAF-4136C566A962");
    private static readonly Guid ItemA = Guid.Parse("7C9E6679-7425-40DE-944B-E07FC1F90AE7");
    private static readonly Guid ItemB = Guid.Parse("D3C8A2F0-5B1E-4A7C-9E26-1F04B8C7E5AA");

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

    private static byte[] AdjustmentHash(string reason, params PostAdjustmentLine[] lines) =>
        RequestHash.Adjustment(ValidAdjustment(new PostAdjustmentRequest(Loc, reason, null, null, lines)));

    private static byte[] ReceiptHash(Guid location, string? reference, params PostReceiptLine[] lines) =>
        RequestHash.Receipt(ValidReceipt(new PostReceiptRequest(location, reference, null, lines)));

    [Fact]
    public void Receipt_canonical_json_is_exact()
    {
        var posting = ValidReceipt(new PostReceiptRequest(
            Loc,
            "  DR-1 ",
            null,
            [new PostReceiptLine(ItemA, 10.0000m, 125.50m), new PostReceiptLine(ItemB, 0.5m, 0m)]));

        RequestHash.CanonicalJson(posting, withUnitCost: true).ShouldBe(
            """{"locationId":"0f8fad5b-d9cb-469f-a165-70867728950e","reference":"DR-1","notes":null,"lines":[{"itemId":"7c9e6679-7425-40de-944b-e07fc1f90ae7","quantity":10,"unitCost":125.5},{"itemId":"d3c8a2f0-5b1e-4a7c-9e26-1f04b8c7e5aa","quantity":0.5,"unitCost":0}]}""");
    }

    [Fact]
    public void Issue_canonical_json_is_exact()
    {
        var posting = ValidIssue(new PostIssueRequest(
            Loc,
            "   ",
            " Sold to Caf\u00e9 ",
            [new PostIssueLine(ItemA, 3m), new PostIssueLine(ItemB, 1.2500m)]));

        RequestHash.CanonicalJson(posting, withUnitCost: false).ShouldBe(
            """{"locationId":"0f8fad5b-d9cb-469f-a165-70867728950e","reference":null,"notes":"Sold to Caf\u00E9","lines":[{"itemId":"7c9e6679-7425-40de-944b-e07fc1f90ae7","quantity":3},{"itemId":"d3c8a2f0-5b1e-4a7c-9e26-1f04b8c7e5aa","quantity":1.25}]}""");
    }

    [Fact]
    public void Lines_are_written_in_LineNo_order()
    {
        ValidatedLine first = new(1, ItemA, 1m, 10m);
        ValidatedLine second = new(2, ItemB, 2m, 20m);

        RequestHash.CanonicalJson(new ValidatedPosting(Loc, null, null, [second, first]), withUnitCost: true)
            .ShouldBe(RequestHash.CanonicalJson(new ValidatedPosting(Loc, null, null, [first, second]), withUnitCost: true));
    }

    [Fact]
    public void Receipt_hash_is_sha256_of_method_route_and_canonical_json()
    {
        var posting = ValidReceipt(new PostReceiptRequest(Loc, null, null, [new PostReceiptLine(ItemA, 1m, 10m)]));
        var preImage = "POST /api/documents/receipts\n" + RequestHash.CanonicalJson(posting, withUnitCost: true);

        RequestHash.Receipt(posting).ShouldBe(SHA256.HashData(Encoding.UTF8.GetBytes(preImage)));
    }

    [Fact]
    public void Issue_hash_is_sha256_of_method_route_and_canonical_json()
    {
        var posting = ValidIssue(new PostIssueRequest(Loc, null, null, [new PostIssueLine(ItemA, 1m)]));
        var preImage = "POST /api/documents/issues\n" + RequestHash.CanonicalJson(posting, withUnitCost: false);

        RequestHash.Issue(posting).ShouldBe(SHA256.HashData(Encoding.UTF8.GetBytes(preImage)));
    }

    [Fact]
    public void The_hash_is_32_bytes()
    {
        var posting = new ValidatedPosting(Loc, null, null, [new ValidatedLine(1, ItemA, 1m, 10m)]);

        RequestHash.Receipt(posting).Length.ShouldBe(32);
        RequestHash.Issue(posting).Length.ShouldBe(32);
    }

    [Fact]
    public void Trailing_zeros_do_not_change_the_hash()
    {
        ReceiptHash(Loc, null, new PostReceiptLine(ItemA, 10m, 5m))
            .ShouldBe(ReceiptHash(Loc, null, new PostReceiptLine(ItemA, 10.0000m, 5.00m)));
    }

    [Fact]
    public void Surrounding_whitespace_does_not_change_the_hash()
    {
        ReceiptHash(Loc, " DR-1", new PostReceiptLine(ItemA, 1m, 5m))
            .ShouldBe(ReceiptHash(Loc, "DR-1", new PostReceiptLine(ItemA, 1m, 5m)));
    }

    [Fact]
    public void A_blank_reference_hashes_like_an_absent_one()
    {
        ReceiptHash(Loc, "   ", new PostReceiptLine(ItemA, 1m, 5m))
            .ShouldBe(ReceiptHash(Loc, null, new PostReceiptLine(ItemA, 1m, 5m)));
    }

    [Fact]
    public void A_changed_quantity_changes_the_hash()
    {
        ReceiptHash(Loc, null, new PostReceiptLine(ItemA, 10m, 5m))
            .ShouldNotBe(ReceiptHash(Loc, null, new PostReceiptLine(ItemA, 10.0001m, 5m)));
    }

    [Fact]
    public void Reordered_lines_change_the_hash()
    {
        PostReceiptLine a = new(ItemA, 1m, 5m);
        PostReceiptLine b = new(ItemB, 2m, 6m);

        ReceiptHash(Loc, null, a, b).ShouldNotBe(ReceiptHash(Loc, null, b, a));
    }

    [Fact]
    public void A_different_location_changes_the_hash()
    {
        ReceiptHash(Loc, null, new PostReceiptLine(ItemA, 1m, 5m))
            .ShouldNotBe(ReceiptHash(OtherLoc, null, new PostReceiptLine(ItemA, 1m, 5m)));
    }

    [Fact]
    public void A_receipt_and_an_issue_of_the_same_lines_hash_differently()
    {
        var posting = new ValidatedPosting(Loc, null, null, [new ValidatedLine(1, ItemA, 1m, null)]);

        RequestHash.Receipt(posting).ShouldNotBe(RequestHash.Issue(posting));
    }

    public static TheoryData<decimal, string> FormatCases => new()
    {
        { 10.0000m, "10" },
        { 0.50m, "0.5" },
        { 0m, "0" },
        { 0.0000m, "0" },
        { 100m, "100" },
        { 100.00m, "100" },
        { 1000000m, "1000000" },
        { 1.50000m, "1.5" },
        { 0.0001m, "0.0001" },
        { 999999999.9999m, "999999999.9999" },
        { 999_999_999_999_999.9999m, "999999999999999.9999" },
    };

    [Theory]
    [MemberData(nameof(FormatCases))]
    public void FormatDecimal_strips_trailing_zeros_without_an_exponent(decimal value, string expected)
    {
        RequestHash.FormatDecimal(value).ShouldBe(expected);
    }

    [Fact]
    public void FormatDecimal_writes_negative_zero_as_zero()
    {
        RequestHash.FormatDecimal(decimal.Negate(0.00m)).ShouldBe("0");
    }

    [Fact]
    public void Transfer_canonical_json_is_exact()
    {
        var posting = ValidTransfer(new PostTransferRequest(
            Loc,
            OtherLoc,
            "  TR-1 ",
            null,
            [new PostTransferLine(ItemA, 4m), new PostTransferLine(ItemB, 0.5000m)]));

        RequestHash.TransferCanonicalJson(posting).ShouldBe(
            """{"fromLocationId":"0f8fad5b-d9cb-469f-a165-70867728950e","toLocationId":"3b241101-e2bb-4255-8caf-4136c566a962","reference":"TR-1","notes":null,"lines":[{"itemId":"7c9e6679-7425-40de-944b-e07fc1f90ae7","quantity":4},{"itemId":"d3c8a2f0-5b1e-4a7c-9e26-1f04b8c7e5aa","quantity":0.5}]}""");
    }

    [Fact]
    public void Adjustment_canonical_json_is_exact()
    {
        var posting = ValidAdjustment(new PostAdjustmentRequest(
            Loc,
            "Damaged",
            null,
            " water damage ",
            [new PostAdjustmentLine(ItemA, -2.0000m, null), new PostAdjustmentLine(ItemB, 5.0000m, 42.50m)]));

        RequestHash.AdjustmentCanonicalJson(posting).ShouldBe(
            """{"locationId":"0f8fad5b-d9cb-469f-a165-70867728950e","reasonCode":"Damaged","reference":null,"notes":"water damage","lines":[{"itemId":"7c9e6679-7425-40de-944b-e07fc1f90ae7","quantityDelta":-2,"unitCost":null},{"itemId":"d3c8a2f0-5b1e-4a7c-9e26-1f04b8c7e5aa","quantityDelta":5,"unitCost":42.5}]}""");
    }

    [Fact]
    public void Transfer_hash_is_sha256_of_method_route_and_canonical_json()
    {
        var posting = ValidTransfer(new PostTransferRequest(Loc, OtherLoc, null, null, [new PostTransferLine(ItemA, 1m)]));
        var preImage = "POST /api/documents/transfers\n" + RequestHash.TransferCanonicalJson(posting);

        RequestHash.Transfer(posting).ShouldBe(SHA256.HashData(Encoding.UTF8.GetBytes(preImage)));
    }

    [Fact]
    public void Adjustment_hash_is_sha256_of_method_route_and_canonical_json()
    {
        var posting = ValidAdjustment(new PostAdjustmentRequest(Loc, "Found", null, null, [new PostAdjustmentLine(ItemA, 1m, 10m)]));
        var preImage = "POST /api/documents/adjustments\n" + RequestHash.AdjustmentCanonicalJson(posting);

        RequestHash.Adjustment(posting).ShouldBe(SHA256.HashData(Encoding.UTF8.GetBytes(preImage)));
    }

    [Fact]
    public void Routes_separate_hashes()
    {
        var receipt = RequestHash.Receipt(ValidReceipt(new PostReceiptRequest(Loc, null, null, [new PostReceiptLine(ItemA, 1m, 10m)])));
        var issue = RequestHash.Issue(ValidIssue(new PostIssueRequest(Loc, null, null, [new PostIssueLine(ItemA, 1m)])));
        var transfer = RequestHash.Transfer(ValidTransfer(new PostTransferRequest(Loc, OtherLoc, null, null, [new PostTransferLine(ItemA, 1m)])));
        var adjustment = AdjustmentHash("Found", new PostAdjustmentLine(ItemA, 1m, 10m));

        receipt.ShouldNotBe(issue);
        receipt.ShouldNotBe(transfer);
        issue.ShouldNotBe(transfer);
        adjustment.ShouldNotBe(receipt);
    }

    [Fact]
    public void A_changed_reason_changes_the_hash()
    {
        AdjustmentHash("Damaged", new PostAdjustmentLine(ItemA, -2m, null))
            .ShouldNotBe(AdjustmentHash("Lost", new PostAdjustmentLine(ItemA, -2m, null)));
    }

    [Fact]
    public void A_flipped_sign_changes_the_hash()
    {
        AdjustmentHash("CountCorrection", new PostAdjustmentLine(ItemA, -2m, null))
            .ShouldNotBe(AdjustmentHash("CountCorrection", new PostAdjustmentLine(ItemA, 2m, null)));
    }

    [Fact]
    public void Swapped_from_and_to_change_the_hash()
    {
        PostTransferLine line = new(ItemA, 1m);

        RequestHash.Transfer(ValidTransfer(new PostTransferRequest(Loc, OtherLoc, null, null, [line])))
            .ShouldNotBe(RequestHash.Transfer(ValidTransfer(new PostTransferRequest(OtherLoc, Loc, null, null, [line]))));
    }

    [Fact]
    public void A_transfer_without_a_destination_cannot_be_hashed()
    {
        var posting = new ValidatedPosting(Loc, null, null, [new ValidatedLine(1, ItemA, 1m, null)]);

        Should.Throw<InvalidOperationException>(() => RequestHash.Transfer(posting));
    }

    [Fact]
    public void An_adjustment_without_a_reason_cannot_be_hashed()
    {
        var posting = new ValidatedPosting(Loc, null, null, [new ValidatedLine(1, ItemA, 1m, null)]);

        Should.Throw<InvalidOperationException>(() => RequestHash.Adjustment(posting));
    }
}