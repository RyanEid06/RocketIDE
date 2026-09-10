namespace RocketIDE.Rocket.Projects;

public interface IRocketTargetDiscovery
{
    RocketTarget? Discover(string activePath);
}
