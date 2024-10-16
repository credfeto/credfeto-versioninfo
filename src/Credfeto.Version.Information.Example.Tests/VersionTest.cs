﻿using FunFair.Test.Common;
using Xunit;
using Xunit.Abstractions;

namespace Credfeto.Version.Information.Example.Tests;

public sealed class VersionTest : LoggingTestBase
{
    public VersionTest(ITestOutputHelper output)
        : base(output)
    {
    }

    [Fact]
    public void Test1()
    {
        this.Output.WriteLine("Hello World");
    }
}