// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using System;

namespace PostSharp.Engineering.BuildTools.Build.Publishing
{
    /// <summary>
    /// An independent publisher that executes a publishing step regardless of created artifact files.
    /// </summary>
    public abstract class IndependentPublisher : Publisher
    {
        /// <summary>
        /// Executes the independent publisher.
        /// </summary>
        public abstract SuccessCode Execute(
            BuildContext context,
            PublishSettings settings,
            BuildArguments buildArguments,
            BuildConfigurationInfo configuration );

        protected sealed override bool Publish(
            BuildContext context,
            PublishSettings settings,
            (string Private, string Public) directories,
            BuildConfigurationInfo configuration,
            BuildArguments buildArguments,
            bool isPublic,
            ref bool hasTarget )
        {
            var success = true;

            switch ( this.Execute( context, settings, buildArguments, configuration ) )
            {
                case SuccessCode.Success:
                    break;

                case SuccessCode.Error:
                    success = false;

                    break;

                case SuccessCode.Fatal:
                    return false;

                default:
                    throw new NotImplementedException();
            }

            if ( !success )
            {
                context.Console.WriteError( "Independent publishing has failed." );
            }

            return success;
        }
    }
}