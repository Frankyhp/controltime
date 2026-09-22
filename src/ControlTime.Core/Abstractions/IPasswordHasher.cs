namespace ControlTime.Core.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);

    bool IsValidFormat(string? passwordHash);
}
