// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Search.Crawlers;

internal record ContentInfo(
    string Breadcrumb,
    string[] Kinds,
    int KindRank,
    string[] Categories,
    string Source,
    string[] Products,
    int ComplexityLevel,
    int ComplexityLevelRank,
    int NavigationLevel,
    bool IsApiDoc );