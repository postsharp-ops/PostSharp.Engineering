// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console;
using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace PostSharp.Engineering.BuildTools.Utilities
{
    [PublicAPI]
    public class ConsoleHelper
    {
        private static readonly CancellationTokenSource _cancellationTokenSource = new();

        static ConsoleHelper()
        {
            Console.CancelKeyPress += OnCancel;
        }

        private static void OnCancel( object? sender, ConsoleCancelEventArgs e ) => _cancellationTokenSource.Cancel();

        public static CancellationToken CancellationToken => _cancellationTokenSource.Token;

        public IAnsiConsole? Out { get; }

        public IAnsiConsole? Error { get; }

        public int ConsoleWidth => this.Out?.Profile.Width ?? Console.WindowWidth;
        
        public int ConsoleHeight => this.Out?.Profile.Height ?? Console.WindowHeight;

        public void WriteError( string format, params object[] args ) => this.WriteError( string.Format( CultureInfo.InvariantCulture, format, args ) );

        public void WriteError( string message )
        {
            if ( this.Error == null )
            {
                Console.Error.WriteLine( message );
            }
            else
            {
                this.Error.MarkupLine( $"[red]{message.EscapeMarkup()}[/]" );
            }
        }

        public void WriteWarning( string message )
        {
            if ( this.Out == null )
            {
                Console.Out.WriteLine( message );
            }
            else
            {
                this.Out.MarkupLine( $"[yellow]{message.EscapeMarkup()}[/]" );
            }
        }

        public void WriteWarning( string format, params object[] args ) => this.WriteWarning( string.Format( CultureInfo.InvariantCulture, format, args ) );

        public void WriteMessage( string message )
        {
            if ( this.Out == null )
            {
                Console.Out.WriteLine( message );
            }
            else
            {
                this.Out.MarkupLine( "[dim]" + message.EscapeMarkup() + "[/]" );
            }
        }

        public void WriteMessage( string format, params object[] args ) => this.WriteMessage( string.Format( CultureInfo.InvariantCulture, format, args ) );

        public void WriteImportantMessage( string message )
        {
            if ( this.Out == null )
            {
                Console.Out.WriteLine( message );
            }
            else
            {
                this.Out.MarkupLine( "[bold]" + message.EscapeMarkup() + "[/]" );
            }
        }

        public void WriteImportantMessage( string format, params object[] args )
            => this.WriteImportantMessage( string.Format( CultureInfo.InvariantCulture, format, args ) );

        public void WriteSuccess( string message )
        {
            if ( this.Out == null )
            {
                Console.Out.WriteLine( message );
            }
            else
            {
                this.Out.MarkupLine( $"[green]{message.EscapeMarkup()}[/]" );
            }
        }

        public void WriteHeading( string message )
        {
            if ( this.Out == null )
            {
                Console.Out.WriteLine( message );
            }
            else
            {
                var rightPaddingLength = 160 - message.Length;

                if ( rightPaddingLength < 3 )
                {
                    rightPaddingLength = 3;
                }

                this.Out.MarkupLine( $"[bold cyan]===== {message.EscapeMarkup()} {new string( '=', rightPaddingLength )}[/]" );
            }
        }

        public void WriteLine()
        {
            if ( this.Out == null )
            {
                Console.Out.WriteLine();
            }
            else
            {
                this.Out.WriteLine();
            }
        }

        public void Write( Table table )
        {
            if ( this.Out == null )
            {
                if ( table.Caption != null )
                {
                    Console.Out.WriteLine( table.Caption.Text );
                }

                Console.Out.WriteLine( "---" );

                foreach ( var row in table.Rows )
                {
                    Console.Write( "| " );
                    
                    foreach ( var cell in row )
                    {
                        Console.Write( cell.ToString() );
                        Console.Write( " | " );
                    }

                    Console.WriteLine( " |" );
                }
            }
            else
            {
                this.Out.Write( table );
            }
        }

        public ConsoleHelper()
        {
            T GetSetting<T>( string name, T defaultValue ) where T : struct
            {
                var value = Environment.GetEnvironmentVariable( name );
                
                return value == null ? defaultValue : Enum.Parse<T>( value );
            }

            var ansi = GetSetting( "ConsoleAnsi", AnsiSupport.Detect );

            if ( ansi == AnsiSupport.No )
            {
                return;
            }
            
            var colorSystem = GetSetting( "ConsoleColorSystem", ColorSystemSupport.Detect );
            var interactive = GetSetting( "ConsoleInteractive", InteractionSupport.Detect );
            
            IAnsiConsole CreateConsole( TextWriter writer )
            {
                return AnsiConsole.Create(
                    new AnsiConsoleSettings
                    {
                        Out = new AnsiConsoleOutputWrapper( writer ), Ansi = ansi, ColorSystem = colorSystem, Interactive = interactive
                    } );
            }

            this.Out = CreateConsole( Console.Out );
            this.Error = CreateConsole( Console.Error );
        }

        protected ConsoleHelper( IAnsiConsole? outConsole, IAnsiConsole? errorConsole )
        {
            this.Out = outConsole;
            this.Error = errorConsole;
        }
    }
}