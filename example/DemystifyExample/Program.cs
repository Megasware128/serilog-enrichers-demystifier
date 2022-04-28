﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .Enrich.WithDemystifiedStackTraces()
    .WriteTo.Console()
    .CreateLogger();

try
{
    return await FailingMethodAsync();
}
catch (Exception ex)
{
    Log.Error(ex, "Unhandled exception");
    return default;
}

[MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
static async Task<int> FailingMethodAsync()
{
    return await Task.FromResult(FailingEnumerator().Sum());
}

[MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
static IEnumerable<int> FailingEnumerator()
{
    yield return 1;
    throw new NotImplementedException();
}
