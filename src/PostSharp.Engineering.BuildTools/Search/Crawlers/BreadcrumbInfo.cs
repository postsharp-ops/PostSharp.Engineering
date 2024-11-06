using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Search.Crawlers;

[PublicAPI]
public record BreadcrumbInfo(
    string Breadcrumb,
    string[] Kinds,
    int KindRank,
    string[] Categories,
    int NavigationLevel,
    bool IsPageIgnored,
    bool IsApiDoc );