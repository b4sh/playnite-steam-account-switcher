using System;
using SteamAccountSwitcher.Core.Steam.Vdf;
using Xunit;

namespace SteamAccountSwitcher.Core.Tests
{
    public class TextVdfTests
    {
        [Fact]
        public void Parses_nested_objects_case_insensitively()
        {
            var root = TextVdf.Parse("\"Root\"\n{\n\t\"Child\"\n\t{\n\t\t\"Key\"\t\t\"value\"\n\t}\n\t\"Other\"\t\"x\"\n}\n");

            Assert.Equal("value", root.GetObject("root").GetObject("CHILD").GetString("key"));
            Assert.Equal("x", root.GetObject("Root").GetString("Other"));
            Assert.Equal(new[] { "Child", "Other" }, root.GetObject("Root").Keys);
        }

        [Fact]
        public void Handles_escapes_comments_bare_tokens_and_conditionals()
        {
            var text = "// header comment\n" +
                       "root { bare token \"quoted\" \"a \\\"b\\\" c\\\\d\" // trailing\n" +
                       " \"cond\" \"1\" [$WIN32]\n }";

            var root = TextVdf.Parse(text).GetObject("root");

            Assert.Equal("token", root.GetString("bare"));
            Assert.Equal("a \"b\" c\\d", root.GetString("quoted"));
            Assert.Equal("1", root.GetString("cond"));
        }

        [Fact]
        public void Duplicate_key_last_wins()
        {
            var root = TextVdf.Parse("\"k\" \"1\" \"K\" \"2\"");

            Assert.Equal("2", root.GetString("k"));
            Assert.Single(root.Keys);
        }

        [Fact]
        public void Wrong_type_lookups_return_null()
        {
            var root = TextVdf.Parse("\"obj\" { } \"str\" \"v\"");

            Assert.Null(root.GetString("obj"));
            Assert.Null(root.GetObject("str"));
            Assert.Null(root.GetString("missing"));
        }

        [Theory]
        [InlineData("\"root\" {")]
        [InlineData("\"root\" { \"k\" \"unterminated }")]
        [InlineData("}")]
        [InlineData("\"key-without-value\"")]
        public void Rejects_malformed_input(string text)
        {
            Assert.Throws<FormatException>(() => TextVdf.Parse(text));
        }
    }
}
