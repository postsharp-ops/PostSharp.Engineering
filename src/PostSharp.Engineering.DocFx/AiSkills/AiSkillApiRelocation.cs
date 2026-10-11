// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.DocFx.AiSkills;

/// <summary>
/// Moves the API files whose name starts with <see cref="FileNamePrefix"/> into the <see cref="Subdirectory"/>
/// of the skill's <c>api/</c> folder, so that searches over the main API do not surface them.
/// </summary>
/// <param name="FileNamePrefix">The prefix of the DocFx YAML file name, for instance <c>PostSharp.</c>.</param>
/// <param name="Subdirectory">The subdirectory of <c>api/</c> that receives the files, for instance <c>migration</c>.</param>
[PublicAPI]
public sealed record AiSkillApiRelocation( string FileNamePrefix, string Subdirectory );
