namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Application.Validators;
using FinanceSentry.Modules.Wealth.Domain;
using FluentAssertions;
using Xunit;

/// <summary>The stated tax profile (#454): nulls stay null, text is trimmed, the code is upper-cased. Synthetic text throughout.</summary>
public sealed class TaxProfileTests
{
    private static readonly Guid UserId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid OtherUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Create_TrimsTheText_AndUppercasesTheJurisdiction()
    {
        var profile = TaxProfile.Create(UserId, " ie ", "  PAYE employee, no side income  ", "  not advice ");

        profile.UserId.Should().Be(UserId);
        profile.Jurisdiction.Should().Be("IE");
        profile.JurisdictionNotes.Should().Be("PAYE employee, no side income");
        profile.Caveat.Should().Be("not advice");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankValue_IsStoredAsNull_NeverAsAnEmptyString(string? blank)
    {
        var profile = TaxProfile.Create(UserId, blank, blank, blank);

        profile.Jurisdiction.Should().BeNull();
        profile.JurisdictionNotes.Should().BeNull();
        profile.Caveat.Should().BeNull();
    }

    [Fact]
    public void Update_RestatesTheWholeProfile_SoAnOmittedFieldIsCleared()
    {
        var profile = TaxProfile.Create(UserId, "IE", "notes", "caveat");

        profile.Update("GB", null, "new caveat");

        profile.Jurisdiction.Should().Be("GB");
        profile.JurisdictionNotes.Should().BeNull();
        profile.Caveat.Should().Be("new caveat");
    }

    [Fact]
    public void TheProfile_IsMarkedAsAuditedProfileData()
        => typeof(TaxProfile).GetCustomAttributes(typeof(FinanceSentry.Infrastructure.Auditing.AuditedProfileDataAttribute), false)
            .Should().ContainSingle().Which.Should().BeOfType<FinanceSentry.Infrastructure.Auditing.AuditedProfileDataAttribute>()
            .Which.ResourceType.Should().Be("tax_profile");

    // --- commands ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Upsert_CreatesTheProfile_ThenRestatesTheSameRow()
    {
        var repository = new FakeTaxProfileRepository();
        var handler = new UpsertTaxProfileCommandHandler(repository);

        var first = await handler.Handle(new UpsertTaxProfileCommand(UserId, new TaxProfileInput("ie", "notes", null)), default);
        var second = await handler.Handle(new UpsertTaxProfileCommand(UserId, new TaxProfileInput("ie", null, "caveat")), default);

        repository.Profiles.Should().ContainSingle();
        first.Jurisdiction.Should().Be("IE");
        first.HasJurisdictionNotes.Should().BeTrue();
        first.HasCaveat.Should().BeFalse();
        second.JurisdictionNotes.Should().BeNull("the second request omitted it, so it is cleared");
        second.Caveat.Should().Be("caveat");
        repository.Saves.Should().Be(1);
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheCallersProfile_AndIsIdempotent()
    {
        var repository = new FakeTaxProfileRepository();
        repository.Profiles.Add(TaxProfile.Create(UserId, "IE", null, null));
        repository.Profiles.Add(TaxProfile.Create(OtherUserId, "IE", null, null));
        var handler = new DeleteTaxProfileCommandHandler(repository);

        await handler.Handle(new DeleteTaxProfileCommand(UserId), default);
        await handler.Handle(new DeleteTaxProfileCommand(UserId), default);

        repository.Profiles.Should().ContainSingle().Which.UserId.Should().Be(OtherUserId);
    }

    // --- query ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task APersonWithNoProfile_GetsTheEmptyOne_WithEveryValueNull()
    {
        var result = await new GetTaxProfileQueryHandler(new FakeTaxProfileRepository()).Handle(new GetTaxProfileQuery(UserId), default);

        result.Should().Be(new TaxProfileDto(true, null, null, null, false, false, null));
        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task TheProfile_ReadsBackWithItsText_AndNeverAnotherPersons()
    {
        var repository = new FakeTaxProfileRepository();
        repository.Profiles.Add(TaxProfile.Create(UserId, "IE", "notes", "caveat"));
        repository.Profiles.Add(TaxProfile.Create(OtherUserId, "GB", "other", "other"));

        var result = await new GetTaxProfileQueryHandler(repository).Handle(new GetTaxProfileQuery(UserId), default);

        result.Readable.Should().BeTrue();
        (result.Jurisdiction, result.JurisdictionNotes, result.Caveat).Should().Be(("IE", "notes", "caveat"));
        result.Disclosure.Should().Be(TaxProfileText.Disclosure);
    }

    [Fact]
    public async Task OnAHostWithoutTheKey_TheTextIsNull_ButTheFlagsSayWhatExists_AndNoEncryptedColumnIsRead()
    {
        var repository = new FakeTaxProfileRepository { CanReadEncryptedFields = false };
        repository.Profiles.Add(TaxProfile.Create(UserId, "IE", "notes", null));

        var result = await new GetTaxProfileQueryHandler(repository).Handle(new GetTaxProfileQuery(UserId), default);

        result.Readable.Should().BeFalse();
        result.Jurisdiction.Should().Be("IE");
        result.JurisdictionNotes.Should().BeNull();
        result.Caveat.Should().BeNull();
        result.HasJurisdictionNotes.Should().BeTrue();
        result.HasCaveat.Should().BeFalse();
        repository.EncryptedColumnReads.Should().Be(0);
    }

    [Fact]
    public async Task OnAHostWithoutTheKey_ANoProfilePerson_IsEmptyAndUnreadable_NotMissingByError()
    {
        var result = await new GetTaxProfileQueryHandler(new FakeTaxProfileRepository { CanReadEncryptedFields = false })
            .Handle(new GetTaxProfileQuery(UserId), default);

        result.IsEmpty.Should().BeTrue();
        result.Readable.Should().BeFalse();
    }
}

public sealed class TaxProfileInputValidatorTests
{
    private static bool IsValid(TaxProfileInput input) => new TaxProfileInputValidator().Validate(input).IsValid;

    [Fact]
    public void AFullInput_IsValid() => IsValid(new TaxProfileInput("IE", "notes", "caveat")).Should().BeTrue();

    [Theory]
    [InlineData("IE", null, null)]
    [InlineData(null, "notes", null)]
    [InlineData(null, null, "caveat")]
    [InlineData("ie", " ", "")]
    public void AnyOneValue_IsEnough(string? jurisdiction, string? notes, string? caveat)
        => IsValid(new TaxProfileInput(jurisdiction, notes, caveat)).Should().BeTrue();

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", " ", "  ")]
    [InlineData("IRL", "notes", null)]
    [InlineData("I", "notes", null)]
    [InlineData("1E", "notes", null)]
    public void NothingAtAll_OrABadCode_IsRefused(string? jurisdiction, string? notes, string? caveat)
        => IsValid(new TaxProfileInput(jurisdiction, notes, caveat)).Should().BeFalse();

    [Fact]
    public void TextOverTheLimit_IsRefused()
    {
        var tooLong = new string('x', TaxProfileLimits.TextMaxLength + 1);
        var atLimit = new string('x', TaxProfileLimits.TextMaxLength);

        IsValid(new TaxProfileInput("IE", tooLong, null)).Should().BeFalse();
        IsValid(new TaxProfileInput("IE", null, tooLong)).Should().BeFalse();
        IsValid(new TaxProfileInput("IE", atLimit, atLimit)).Should().BeTrue();
    }

    [Fact]
    public void TheCommandValidator_RefusesAMissingInput()
        => new UpsertTaxProfileCommandValidator().Validate(new UpsertTaxProfileCommand(Guid.NewGuid(), null!)).IsValid.Should().BeFalse();
}
