using SteamAccountSwitcher.Core.Steam;
using SteamAccountSwitcher.Core.Tests.Fakes;
using Xunit;

namespace SteamAccountSwitcher.Core.Tests
{
    public class SteamLocatorTests
    {
        [Fact]
        public void Uses_registry_and_normalizes_forward_slashes()
        {
            var env = new FakeSteamEnvironment();
            env.SetUserValue(null, "SteamPath", "c:/program files (x86)/steam");
            env.SetUserValue(null, "SteamExe", "c:/program files (x86)/steam/steam.exe");

            var steam = SteamLocator.Locate(env, null);

            Assert.Equal(@"c:\program files (x86)\steam", steam.RootPath);
            Assert.Equal(@"c:\program files (x86)\steam\steam.exe", steam.ExePath);
            Assert.Equal(@"c:\program files (x86)\steam\config\loginusers.vdf", steam.LoginUsersPath);
        }

        [Fact]
        public void Derives_exe_when_SteamExe_is_missing()
        {
            var env = new FakeSteamEnvironment();
            env.SetUserValue(null, "SteamPath", "d:/steam/");

            Assert.Equal(@"d:\steam\steam.exe", SteamLocator.Locate(env, null).ExePath);
        }

        [Fact]
        public void Override_wins_over_registry()
        {
            var env = new FakeSteamEnvironment();
            env.SetUserValue(null, "SteamPath", "c:/steam");

            var steam = SteamLocator.Locate(env, @"E:\Games\Steam\");

            Assert.Equal(@"E:\Games\Steam", steam.RootPath);
            Assert.Equal(@"E:\Games\Steam\steam.exe", steam.ExePath);
        }

        [Fact]
        public void Falls_back_to_machine_install_path()
        {
            var env = new FakeSteamEnvironment { MachineInstallPath = @"C:\Program Files (x86)\Steam" };

            Assert.Equal(@"C:\Program Files (x86)\Steam\steam.exe", SteamLocator.Locate(env, "  ").ExePath);
        }

        [Fact]
        public void Returns_null_when_not_installed()
        {
            Assert.Null(SteamLocator.Locate(new FakeSteamEnvironment(), null));
        }
    }
}
