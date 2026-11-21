using ThoriumInbox.Core;

namespace ThoriumInbox.Tests;

public sealed class ThoriumLibraryReceiptTests
{
    [Fact]
    public void OpenReaderConfirmsAnImportThroughItsPublicationPatch()
    {
        var profile = Directory.CreateTempSubdirectory("thorium-inbox-reader-test-");
        try
        {
            var config = profile.CreateSubdirectory("config-data-json");
            string patches = Path.Combine(config.FullName, "state.patch.json");
            File.WriteAllText(Path.Combine(config.FullName, "state.json"), """{"publication":{"db":{}}}""");
            const string fingerprint = "ed6a0b80272c1c9db98f57428766fa45f48475ad";
            Assert.False(ThoriumLibraryReceipt.HasImport(profile.FullName, fingerprint));

            File.WriteAllText(patches, """
                {"op":"replace","path":"/i18n/locale","value":"de"},
                {"op":"add","path":"/publication/db/book-id","value":{"hash":"ed6a0b80272c1c9db98f57428766fa45f48475ad"}},
                """ + Environment.NewLine);
            Assert.True(ThoriumLibraryReceipt.HasImport(profile.FullName, fingerprint));
            Assert.False(ThoriumLibraryReceipt.HasImport(profile.FullName, "unrelated-file"));

            File.AppendAllText(patches, """{"op":"remove","path":"/publication/db/book-id"},""");
            Assert.False(ThoriumLibraryReceipt.HasImport(profile.FullName, fingerprint));
        }
        finally
        {
            profile.Delete(recursive: true);
        }
    }
}
