using System.Security.Cryptography;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/GeniusWeb.Tools.PasswordHash -- <password>");
    return 1;
}

const int saltSize = 16;
const int hashSize = 32;
const int iterations = 210_000;

var salt = RandomNumberGenerator.GetBytes(saltSize);
var hash = Rfc2898DeriveBytes.Pbkdf2(args[0], salt, iterations, HashAlgorithmName.SHA256, hashSize);
Console.WriteLine($"pbkdf2.sha256.{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}");
return 0;
