using System;
using System.IO;
using Chess.Net.Cloud;
using Shouldly;
using Xunit;

namespace Chess.Tests.Cloud;

/// <summary>
/// Which firebase-config.json a desktop head reads: the first candidate holding a JSON object, and none at
/// all (no "Online game") when there is no such file, which is every fork's and PR build's normal state.
/// </summary>
public sealed class CloudConfigFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("chess-cloudcfg-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void No_file_anywhere_means_no_backend()
    {
        CloudConfigFile.TryLoad(Path.Combine(_dir, "a.json"), Path.Combine(_dir, "b.json")).ShouldBeNull();
    }

    [Fact]
    public void The_first_candidate_wins_so_a_shipped_config_beats_the_user_copy()
    {
        var shipped = Write("shipped.json", """{"projectId":"shipped"}""");
        var user = Write("user.json", """{"projectId":"user"}""");

        CloudConfigFile.TryLoad(shipped, user).ShouldContain("shipped");
    }

    [Fact]
    public void A_missing_first_candidate_falls_through_to_the_user_copy()
    {
        var user = Write("user.json", """  {"projectId":"user"}  """);

        CloudConfigFile.TryLoad(Path.Combine(_dir, "absent.json"), user).ShouldBe("""{"projectId":"user"}""");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TODO: paste the config here")]
    public void A_file_that_is_not_a_JSON_object_reads_as_absent(string text)
    {
        var placeholder = Write("placeholder.json", text);
        var user = Write("user.json", """{"projectId":"user"}""");

        CloudConfigFile.TryLoad(placeholder).ShouldBeNull();
        CloudConfigFile.TryLoad(placeholder, user).ShouldContain("user");
    }

    [Fact]
    public void The_user_copy_lives_outside_any_checkout_under_the_app_data_folder()
    {
        CloudConfigFile.UserPath.ShouldBe(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "chess", "firebase-config.json"));
    }
}
