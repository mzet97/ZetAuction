using ZetAuction.Domain.Users;

namespace ZetAuction.UnitTests.Domain;

public class UserTests
{
    [Fact]
    public void Constructor_Should_Initialize_All_Properties_When_Data_Is_Provided()
    {
        var user = new User("Test User", "test@example.com", "hash", Role.Admin);

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("Test User", user.Name);
        Assert.Equal("test@example.com", user.Email);
        Assert.Equal("hash", user.PasswordHash);
        Assert.Equal(Role.Admin, user.Role);
        Assert.False(user.IsDeleted);
        Assert.Null(user.DeletedAtUtc);
        Assert.True(user.IsValid());
    }

    [Fact]
    public void IsValid_Should_Return_True_When_User_Is_Valid()
    {
        var user = new User("Test User", "test@example.com", "hash", Role.User);

        Assert.True(user.IsValid());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void IsValid_Should_Return_False_When_Name_Is_Empty(string name)
    {
        var user = new User(name, "test@example.com", "hash", Role.User);

        Assert.False(user.IsValid());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void IsValid_Should_Return_False_When_Email_Is_Empty(string email)
    {
        var user = new User("Test User", email, "hash", Role.User);

        Assert.False(user.IsValid());
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData("test@")]
    public void IsValid_Should_Return_False_When_Email_Is_Invalid(string email)
    {
        var user = new User("Test User", email, "hash", Role.User);

        Assert.False(user.IsValid());
    }

    [Fact]
    public void IsValid_Should_Return_False_When_Role_Is_Invalid()
    {
        var user = new User("Test User", "test@example.com", "hash", (Role)999);

        Assert.False(user.IsValid());
    }

    [Fact]
    public void UpdateProfile_Should_Update_Name_And_Email_When_Data_Is_Provided()
    {
        var user = new User("Test User", "test@example.com", "hash", Role.User);

        user.UpdateProfile("Updated User", "updated@example.com");

        Assert.Equal("Updated User", user.Name);
        Assert.Equal("updated@example.com", user.Email);
    }

    [Fact]
    public void ChangePassword_Should_Update_PasswordHash_When_Hash_Is_Provided()
    {
        var user = new User("Test User", "test@example.com", "old-hash", Role.User);

        user.ChangePassword("new-hash");

        Assert.Equal("new-hash", user.PasswordHash);
    }

    [Fact]
    public void ChangeRole_Should_Update_Role_When_Role_Is_Provided()
    {
        var user = new User("Test User", "test@example.com", "hash", Role.User);

        user.ChangeRole(Role.Admin);

        Assert.Equal(Role.Admin, user.Role);
    }

    [Fact]
    public void Validate_Should_Return_Specific_Errors_When_User_Is_Invalid()
    {
        var user = new User("", "invalid-email", "hash", (Role)999);

        var result = user.Validate();
        var messages = result.Errors.Select(error => error.ErrorMessage).ToArray();

        Assert.False(result.IsValid);
        Assert.Contains("Name is required.", messages);
        Assert.Contains("A valid email address is required.", messages);
        Assert.Contains("A valid role must be specified.", messages);
    }

    [Fact]
    public void SoftDelete_Should_Set_IsDeleted_And_DeletedAtUtc_When_Called()
    {
        var user = new User("Test User", "test@example.com", "hash", Role.User);

        user.SoftDelete(TestHelpers.FixedUtcNow);

        Assert.True(user.IsDeleted);
        Assert.Equal(TestHelpers.FixedUtcNow, user.DeletedAtUtc);
    }
}
