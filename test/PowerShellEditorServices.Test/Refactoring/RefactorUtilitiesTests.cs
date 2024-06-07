// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerShell.EditorServices.Services;
using Microsoft.PowerShell.EditorServices.Services.PowerShell.Host;
using Microsoft.PowerShell.EditorServices.Services.TextDocument;
using Microsoft.PowerShell.EditorServices.Test;
using Microsoft.PowerShell.EditorServices.Test.Shared;
using Microsoft.PowerShell.EditorServices.Handlers;
using Xunit;
using System.Management.Automation.Language;
using Microsoft.PowerShell.EditorServices.Refactoring;
using System.Management.Automation.Language;
using System.Collections.Generic;
using System.Linq;

namespace PowerShellEditorServices.Test.Refactoring
{
    [Trait("Category", "RefactorUtilities")]
    public class RefactorUtilitiesTests : IAsyncLifetime
    {
        private PsesInternalHost psesHost;
        private WorkspaceService workspace;

        public async Task InitializeAsync()
        {
            psesHost = await PsesHostFactory.Create(NullLoggerFactory.Instance);
            workspace = new WorkspaceService(NullLoggerFactory.Instance);
        }

        public async Task DisposeAsync() => await Task.Run(psesHost.StopAsync);
        private ScriptFile GetTestScript(string fileName) => workspace.GetFile(TestUtilities.GetSharedPath(Path.Combine("Refactoring", "Utilities", fileName)));

        [Fact]
        public void GetVariableExpressionAst()
        {
            RenameSymbolParams request = new()
            {
                Column = 11,
                Line = 15,
                RenameTo = "Renamed",
                FileName = "TestDetection.ps1"
            };
            ScriptFile scriptFile = GetTestScript(request.FileName);

            Ast symbol = Utilities.GetAst(request.Line, request.Column, scriptFile.ScriptAst);
            Assert.Equal(15, symbol.Extent.StartLineNumber);
            Assert.Equal(1, symbol.Extent.StartColumnNumber);

        }
        [Fact]
        public void GetVariableExpressionStartAst()
        {
            RenameSymbolParams request = new()
            {
                Column = 1,
                Line = 15,
                RenameTo = "Renamed",
                FileName = "TestDetection.ps1"
            };
            ScriptFile scriptFile = GetTestScript(request.FileName);

            Ast symbol = Utilities.GetAst(request.Line, request.Column, scriptFile.ScriptAst);
            Assert.Equal(15, symbol.Extent.StartLineNumber);
            Assert.Equal(1, symbol.Extent.StartColumnNumber);

        }
        [Fact]
        public void GetVariableWithinParameterAst()
        {
            RenameSymbolParams request = new()
            {
                Column = 21,
                Line = 3,
                RenameTo = "Renamed",
                FileName = "TestDetection.ps1"
            };
            ScriptFile scriptFile = GetTestScript(request.FileName);

            Ast symbol = Utilities.GetAst(request.Line, request.Column, scriptFile.ScriptAst);
            Assert.Equal(3, symbol.Extent.StartLineNumber);
            Assert.Equal(17, symbol.Extent.StartColumnNumber);

        }
        [Fact]
        public void GetHashTableKey()
        {
            RenameSymbolParams request = new()
            {
                Column = 9,
                Line = 16,
                RenameTo = "Renamed",
                FileName = "TestDetection.ps1"
            };
            ScriptFile scriptFile = GetTestScript(request.FileName);

            Ast symbol = Utilities.GetAst(request.Line, request.Column, scriptFile.ScriptAst);
            Assert.Equal(16, symbol.Extent.StartLineNumber);
            Assert.Equal(5, symbol.Extent.StartColumnNumber);

        }
        [Fact]
        public void GetVariableWithinCommandAst()
        {
            RenameSymbolParams request = new()
            {
                Column = 29,
                Line = 6,
                RenameTo = "Renamed",
                FileName = "TestDetection.ps1"
            };
            ScriptFile scriptFile = GetTestScript(request.FileName);

            Ast symbol = Utilities.GetAst(request.Line, request.Column, scriptFile.ScriptAst);
            Assert.Equal(6, symbol.Extent.StartLineNumber);
            Assert.Equal(28, symbol.Extent.StartColumnNumber);

        }
        [Fact]
        public void GetCommandParameterAst()
        {
            RenameSymbolParams request = new()
            {
                Column = 12,
                Line = 21,
                RenameTo = "Renamed",
                FileName = "TestDetection.ps1"
            };
            ScriptFile scriptFile = GetTestScript(request.FileName);

            Ast symbol = Utilities.GetAst(request.Line, request.Column, scriptFile.ScriptAst);
            Assert.Equal(21, symbol.Extent.StartLineNumber);
            Assert.Equal(10, symbol.Extent.StartColumnNumber);

        }
        [Fact]
        public void GetFunctionDefinitionAst()
        {
            RenameSymbolParams request = new()
            {
                Column = 12,
                Line = 1,
                RenameTo = "Renamed",
                FileName = "TestDetection.ps1"
            };
            ScriptFile scriptFile = GetTestScript(request.FileName);

            Ast symbol = Utilities.GetAst(request.Line, request.Column, scriptFile.ScriptAst);
            Assert.Equal(1, symbol.Extent.StartLineNumber);
            Assert.Equal(1, symbol.Extent.StartColumnNumber);

        }
        [Fact]
        public void GetVariableUnderFunctionDef()
        {
            RenameSymbolParams request = new(){
                Column=5,
                Line=2,
                RenameTo="Renamed",
                FileName="TestDetectionUnderFunctionDef.ps1"
            };
            ScriptFile scriptFile = GetTestScript(request.FileName);

            Ast symbol = Utilities.GetAst(request.Line,request.Column,scriptFile.ScriptAst);
            Assert.IsType<VariableExpressionAst>(symbol);
            Assert.Equal(2,symbol.Extent.StartLineNumber);
            Assert.Equal(5,symbol.Extent.StartColumnNumber);

        }
        [Fact]
        public void AssertContainsDotSourcingTrue()
        {
            ScriptFile scriptFile = GetTestScript("TestDotSourcingTrue.ps1");
            Assert.True(Utilities.AssertContainsDotSourced(scriptFile.ScriptAst));
        }
        [Fact]
        public void AssertContainsDotSourcingFalse()
        {
            ScriptFile scriptFile = GetTestScript("TestDotSourcingFalse.ps1");
            Assert.False(Utilities.AssertContainsDotSourced(scriptFile.ScriptAst));
        }
    }
}