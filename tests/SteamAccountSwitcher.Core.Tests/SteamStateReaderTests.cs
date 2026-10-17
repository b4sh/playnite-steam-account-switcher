using SteamAccountSwitcher.Core.Steam;
using SteamAccountSwitcher.Core.Tests.Fakes;
using Xunit;

namespace SteamAccountSwitcher.Core.Tests
{
    public class SteamStateReaderTests
    {
        [Fact]
        public void Not_running_when_registry_is_empty()
        {
            Assert.False(SteamStateReader.Read(new FakeSteamEnvironment()).IsRunning);
        }

        [Fact]
        public void Not_running_when_pid_is_zero()
        {
            var env = new FakeSteamEnvironment();
            env.SetUserValue("ActiveProcess", "pid", 0u);
            env.SetUserValue("ActiveProcess", "ActiveUser", 0u);

            Assert.False(SteamStateReader.Read(env).IsRunning);
        }

        [Fact]
        public void Stale_pid_of_dead_process_is_not_running()
        {
            var env = new FakeSteamEnvironment();
            env.SetUserValue("ActiveProcess", "pid", 4242u);
            env.SetUserValue("ActiveProcess", "ActiveUser", 22202u);

            var state = SteamStateReader.Read(env);

            Assert.False(state.IsRunning);
            Assert.Equal(0u, state.ActiveUser);
        }

        [Fact]
        public void Stale_pid_reused_by_another_process_is_not_running()
        {
            var env = new FakeSteamEnvironment();
            env.SetSteamRunning(4242, 22202);
            env.Processes[4242] = "notepad";

            Assert.False(SteamStateReader.Read(env).IsRunning);
        }

        [Fact]
        public void Reads_running_state_with_game()
        {
            var env = new FakeSteamEnvironment();
            env.SetSteamRunning(4242, 22202, runningAppId: 2398030);
            env.Processes[4242] = "Steam";
            env.SetUserValue(@"Apps\2398030", "Name", "Some Game");

            var state = SteamStateReader.Read(env);

            Assert.True(state.IsRunning);
            Assert.Equal(4242, state.ProcessId);
            Assert.Equal(22202u, state.ActiveUser);
            Assert.Equal(2398030u, state.RunningAppId);
            Assert.Equal("Some Game", state.RunningAppName);
        }

        [Fact]
        public void Logging_in_has_ActiveUser_zero()
        {
            var env = new FakeSteamEnvironment();
            env.SetSteamRunning(4242, 0);

            var state = SteamStateReader.Read(env);

            Assert.True(state.IsRunning);
            Assert.Equal(0u, state.ActiveUser);
            Assert.Null(state.RunningAppName);
        }
    }
}
