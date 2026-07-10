# Publishing flow

```mermaid
flowchart TB
    start([b publish])
    success([Success])
    failure([Failure])

    start --> dependencies_published{Are all changes<br>of all dependencies<br>published?}
    
    dependencies_published --> |Yes| any_changes{Are there<br>any changes<br>since the latest<br>publishing tag?}
    dependencies_published --> |No| failure

    any_changes --> |Yes| is_version_bumped{Is the main<br>version bumped?}
    any_changes --> |No| success

    is_version_bumped --> |Yes| publish[Publish]
    is_version_bumped --> |No| manual_version_bump[Require manual version bump] --> failure

    publish --> is_published{Was the publishing<br>successful?}

    is_published --> |Yes| tag[Tag the published commit] --> version_bump[Bump the main version]
    is_published --> |No| failure

    version_bump --> requires_merge{Does the repo<br>require merge<br>to the master branch?}

    requires_merge --> |Yes| merge[Merge the published commit to master] --> success
    requires_merge --> |No| success
```

## Web sites

A web site is published by `MsDeployPublisher` to an Azure AppService deployment slot, then promoted to
production by `AppServiceSwapper`. The staging slot is stopped whenever no deployment is in progress: after a
swap it runs the application that was previously in production, which we do not want to keep running.

```mermaid
flowchart TB
    publish([b publish]) --> msdeploy[MSDeploy the package<br>to the staging slot]
    msdeploy --> start[Start the staging slot]
    start --> publisher_testers[Run the testers of the publisher<br>against the staging slot]
    publisher_testers --> swap([b swap])
    swap --> start_again[Start the staging slot]
    start_again --> do_swap[Swap the staging slot<br>into the production slot]
    do_swap --> swapper_testers{Do the testers of the swapper<br>pass against the production slot?}
    swapper_testers --> |Yes| stop[Stop the staging slot] --> success([Success])
    swapper_testers --> |No| revert[Swap back to revert,<br>leaving the staging slot running] --> failure([Failure])
```

The swap runs either inline at the end of `b publish`, when `BuildConfigurationInfo.SwapAfterPublishing` is
set, or as a separate `b swap` step, which TeamCity exposes as its own build configuration. The staging slot
is started twice because these two steps can run hours apart.

Deploying to a stopped slot works because stopping a slot does not stop its SCM site, through which MSDeploy
works. Conversely, a slot must be running to be swapped: Azure restarts and warms up every instance of the
source slot and aborts the swap if any of them fails to answer an HTTP request. This is why the slot is
started before the swap and stopped only after it.

The staging slot is left running after a failed or reverted swap, so that the failed deployment can be
investigated and swapped back manually. It is stopped when the swap is skipped, which happens for a
pre-release when `Swapper.SwapPrerelease` is not set.

These properties opt out of the behavior:

| Property | Default | Effect when `false` |
|---|---|---|
| `MsDeployConfiguration.StartSlotAfterDeployment` | `true` | The slot is not started after the package has been deployed to it. |
| `AppServiceSwapper.StartSourceSlotBeforeSwap` | `true` | The source slot is not started before the swap. |
| `AppServiceSwapper.StopSourceSlotAfterSwap` | `true` | The source slot is left running after the swap. |