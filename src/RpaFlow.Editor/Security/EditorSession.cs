using System.Security.Cryptography;
using System.Text;

namespace RpaFlow.Editor.Security;

public sealed class EditorSession
{
    private readonly string _token = Convert.ToHexString(
        RandomNumberGenerator.GetBytes(32));

    public string Token => _token;

    public bool IsAuthorized(HttpRequest request)
    {
        var candidate = request.Headers["X-Editor-Token"].ToString();
        if (candidate.Length != _token.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate),
            Encoding.UTF8.GetBytes(_token));
    }
}
