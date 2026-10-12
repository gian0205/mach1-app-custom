using System;
using System.IO;

namespace Mach1DriverCustom.Services;

/// <summary>
/// Proteção contra curva instável aplicada no logon: um arquivo marcador é criado antes de
/// aplicar e só é removido se o sistema continuar de pé por alguns minutos (ou se o app fechar
/// normalmente). Se o marcador ainda existir no logon seguinte, o PC caiu logo depois da última
/// aplicação, e a curva deixa de ser aplicada até o usuário salvar um perfil novo.
/// </summary>
public static class StartupGuard
{
    private static string PendingFlag => Path.Combine(ProfileStore.Directory, "applying.flag");
    private static string BlockedFlag => Path.Combine(ProfileStore.Directory, "blocked.flag");

    public static bool IsBlocked => File.Exists(BlockedFlag);

    /// <summary>Chamar antes de aplicar no logon; false significa que a aplicação está bloqueada.</summary>
    public static bool Begin()
    {
        Directory.CreateDirectory(ProfileStore.Directory);

        if (File.Exists(PendingFlag))
        {
            File.Move(PendingFlag, BlockedFlag, overwrite: true);
            return false;
        }

        if (IsBlocked)
            return false;

        File.WriteAllText(PendingFlag, DateTime.Now.ToString("O"));
        return true;
    }

    /// <summary>O sistema sobreviveu à aplicação.</summary>
    public static void Confirm() => File.Delete(PendingFlag);

    public static void ClearBlock()
    {
        File.Delete(BlockedFlag);
        File.Delete(PendingFlag);
    }
}
