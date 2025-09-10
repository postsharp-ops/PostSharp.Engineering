namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;

public class TeamCityEngineeringPrepareImageBuildStep : TeamCityPowerShellBuildStep
{
    private static string GetCustomArgumentsParameterName( string objectName ) => $"{objectName}Arguments";

    public TeamCityEngineeringPrepareImageBuildStep(
        string id,
        string name,
        DockerSpec dockerSpec ) : base(
        id,
        name,
        $"DockerBuild.ps1",
        $"-BuildImage -ImageName {dockerSpec.ImageName}" )
    {
    }
}