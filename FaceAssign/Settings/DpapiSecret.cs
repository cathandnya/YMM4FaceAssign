using System.Security.Cryptography;
using System.Text;

namespace FaceAssign.Settings;

/// <summary>
/// APIキーを設定ファイルに平文で残さないための小さなラッパー。
///
/// DPAPI の CurrentUser スコープなので、**同じ Windows ユーザーで動くプロセスなら復号できる**。
/// 守れるのは「設定ファイルを他のマシンや他のユーザーに持ち出されたとき」であって、
/// このPCで動くマルウェアからは守れない。
/// </summary>
internal static class DpapiSecret
{
    public static string Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return "";

        try
        {
            var cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plainText), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipher);
        }
        catch (Exception)
        {
            // 暗号化できない環境なら、平文で書くより保存しないほうがまし。
            return "";
        }
    }

    public static string Unprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
            return "";

        try
        {
            var plain = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedText), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception)
        {
            // 別ユーザー・別マシンの設定ファイルを読んだ場合はここに来る。空で扱う。
            return "";
        }
    }
}
