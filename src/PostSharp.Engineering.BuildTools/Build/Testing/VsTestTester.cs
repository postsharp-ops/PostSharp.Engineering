// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace PostSharp.Engineering.BuildTools.Build.Testing
{
    /// <summary>
    /// An implementation of <see cref="Tester"/> for VSTest.
    /// </summary>
    public class VsTestTester : Tester
    {
        public ParametricString TestPackageName { get; init; }

        public string TestAssemblyName { get; init; }

        public (string Name, string Value)[] EnvironmentVariables { get; init; } = [];

        public VsTestTester( ParametricString testPackageName, string testAssemblyName )
        {
            this.TestPackageName = testPackageName;
            this.TestAssemblyName = testAssemblyName;
        }

        public override SuccessCode Execute(
            BuildContext context,
            string artifactsDirectory,
            BuildInfo buildInfo,
            bool dry )
        {
            var tempDirectory = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );
            Directory.CreateDirectory( tempDirectory );

            try
            {
                var packagePath = Path.Combine( artifactsDirectory, this.TestPackageName.ToString( buildInfo ) );
                ZipFile.ExtractToDirectory( packagePath, tempDirectory );

                var exe = @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\Extensions\TestPlatform\vstest.console.exe";

                var argsList = new List<string>();

                foreach ( var environmentVariable in this.EnvironmentVariables )
                {
                    argsList.Add( $"-e:{environmentVariable.Name}=\"{environmentVariable.Value}\"" );
                }

                argsList.Add( this.TestAssemblyName );

                var args = string.Join( ' ', argsList );

                if ( dry )
                {
                    context.Console.WriteImportantMessage( $"Dry run: {exe} {args}" );

                    return SuccessCode.Success;
                }
                else
                {
                    return ToolInvocationHelper.InvokeTool(
                        context.Console,
                        exe,
                        args,
                        tempDirectory )
                        ? SuccessCode.Success
                        : SuccessCode.Error;
                }
            }
            finally
            {
                Directory.Delete( tempDirectory, true );
            }
        }
    }
}