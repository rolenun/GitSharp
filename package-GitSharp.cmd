tools\nant\nant.exe -buildfile:GitSharp.build %1 -t:net-3.5 -D:build.config=release -D:build.vcs.number.1=%BUILD_VCS_NUMBER% clean dist
