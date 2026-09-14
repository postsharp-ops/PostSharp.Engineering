This project contains system types (well-known types recognized by the compiler) that are not included on legacy platforms.

They allow to use new C# features on old platforms.

The project produces nothing that is shipped. `PostSharp.Engineering.Sdk` packs these files as assets, and
`SystemTypes.props` adds them as `Compile` items to the consuming project, so they are compiled under the target
framework and the compiler settings of that project rather than under this one. This project exists to fail here
instead of there, and its settings are chosen for that:

- `LangVersion` is 10, the lowest version of C# that has to be supported.
- The target frameworks are `net472` and `netstandard2.0`, the legacy frameworks a consumer uses. Neither declares
  `ReadOnlySpan<T>` or the nullable attributes, so a reference to a type that this repository always has is caught.
- `GenerateDocumentationFile` is on, because the compiler resolves a `<see cref>` only when it writes the
  documentation file. Without it, an unresolvable reference compiles here and breaks a consumer that documents its
  own API.
- `TreatWarningsAsErrors` is on for every build, not only on the build server, because a warning here is an error in
  a consuming repository.

When a reference cannot resolve in one of these frameworks because the type does not exist there, write it as `<c>`
rather than as `<see cref>`. That is the one case in which `<c>` is the accurate markup and not a way around a
documentation error.
