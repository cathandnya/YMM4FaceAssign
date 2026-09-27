using System.Reflection;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Tachie;
using YukkuriMovieMaker.Project;

namespace FaceAssign.Tachie;

/// <summary>キャラクターから立ち絵の情報を引き出す。</summary>
public static class TachieCharacterProbe
{
    public const string AnimationTachiePluginTypeName =
        "YukkuriMovieMaker.Plugin.Tachie.AnimationTachie.AnimationTachiePlugin";

    /// <summary>動く立ち絵を使っているキャラクターかどうか。</summary>
    public static bool IsAnimationTachie(Character character)
        => character.TachieType?.FullName == AnimationTachiePluginTypeName;

    /// <summary>
    /// 素材フォルダのパス。ITachieCharacterParameter はメンバを宣言していないマーカーなので
    /// Directory プロパティをリフレクションで取る。
    /// </summary>
    public static string? GetTachieDirectory(Character character)
    {
        var parameter = character.TachieCharacterParameter;
        if (parameter is null)
            return null;

        var property = parameter.GetType().GetProperty("Directory", BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(parameter) as string;
    }

    /// <summary>そのキャラクターの立ち絵プラグインが作る、空の表情パラメータ。</summary>
    public static ITachieFaceParameter? CreateFaceParameter(Character character)
    {
        if (character.TachieType is null)
            return null;

        return (PluginLoader.GetPlugin(character.TachieType) as ITachiePlugin)?.CreateFaceParameter();
    }
}
