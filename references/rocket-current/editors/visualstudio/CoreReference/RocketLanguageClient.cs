using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Rocket.VisualStudio.Core;

namespace Rocket.VisualStudio
{
#pragma warning disable 0649
    internal static class RocketContentDefinition
    {
        [Export]
        [Name("Rocket")]
        [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
        internal static ContentTypeDefinition RocketContentTypeDefinition;

        [Export]
        [FileExtension(".rocket")]
        [ContentType("Rocket")]
        internal static FileExtensionToContentTypeDefinition RocketFileExtensionDefinition;
    }
#pragma warning restore 0649

    [Export(typeof(ILanguageClient))]
    [ContentType("Rocket")]
    internal sealed class RocketLanguageClient : ILanguageClient
    {
        private System.Diagnostics.Process process;

        [Import(typeof(Microsoft.VisualStudio.Shell.SVsServiceProvider))]
        internal IServiceProvider ServiceProvider { get; set; }

        public string Name => "Rocket Language Server";
        public IEnumerable<string> ConfigurationSections { get { yield return "rocket"; } }
        public object InitializationOptions => null;
        public IEnumerable<string> FilesToWatch => null;
        public bool ShowNotificationOnInitializeFailed => true;
#pragma warning disable 0067
        public event AsyncEventHandler<EventArgs> StartAsync;
        public event AsyncEventHandler<EventArgs> StopAsync;
#pragma warning restore 0067

        public async Task<Connection> ActivateAsync(CancellationToken token)
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            var dte = ServiceProvider?.GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            var activePath = dte?.ActiveDocument?.FullName;
            var options = RocketPackage.GetOptionsSnapshot();
            var compiler = RocketToolLocator.FindCompiler(options.CompilerPath, activePath);
            var server = RocketToolLocator.FindLanguageServer(options.LanguageServerPath, activePath, compiler);
            if (server == null) throw new FileNotFoundException("rocket-lsp.exe was not found. Configure Tools > Options > Rocket or build the active Rocket workspace.");
            var environment = await RocketEnvironmentLoader.LoadAsync(activePath ?? Directory.GetCurrentDirectory(), options.LoadPinnedEnvironment).ConfigureAwait(false);
            var startInfo = new ProcessStartInfo
            {
                FileName = server,
                WorkingDirectory = string.IsNullOrWhiteSpace(activePath) ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(activePath),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var item in environment) startInfo.EnvironmentVariables[item.Key] = item.Value;
            process = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null) RocketPackage.WriteLanguageServerLog(args.Data);
            };
            if (!process.Start()) return null;
            process.BeginErrorReadLine();
            token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } });
            return new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
        }

        public async Task OnLoadedAsync()
        {
            var handler = StartAsync;
            if (handler != null) await handler.InvokeAsync(this, EventArgs.Empty);
        }

        public Task<InitializationFailureContext> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationInfo)
        {
            var message = initializationInfo?.InitializationException?.Message ?? initializationInfo?.StatusMessage ?? "unknown failure";
            RocketPackage.WriteLanguageServerLog("Initialization failed: " + message);
            return Task.FromResult(new InitializationFailureContext { FailureMessage = "Rocket language server initialization failed: " + message });
        }

        public Task OnServerInitializedAsync()
        {
            RocketPackage.WriteLanguageServerLog("rocket-lsp initialized");
            return Task.CompletedTask;
        }
    }
}
