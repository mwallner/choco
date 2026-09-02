// Copyright © 2017 - 2025 Chocolatey Software, Inc
// Copyright © 2011 - 2017 RealDimensions Software, LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
// You may obtain a copy of the License at
//
// 	http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using chocolatey.infrastructure.information;
using FluentAssertions;

namespace chocolatey.tests.infrastructure.information
{
    public class ProcessInformationSpecs
    {
        public abstract class ProcessInformationSpecsBase : TinySpec
        {
            public override void Context()
            {
            }

            protected static Process CreateProcessWithId(int id, string name)
            {
                // The Process class caches its id/name in private fields that are only populated
                // by EnsureState() when unset. On .NET Framework, ProcessName is read from an
                // internal System.Diagnostics.ProcessInfo object rather than a field on Process itself,
                // so that object has to be faked too in order to avoid a real OS lookup.
                var process = new Process();

                var processInfoType = typeof(Process).Assembly.GetType("System.Diagnostics.ProcessInfo");
                var processInfo = Activator.CreateInstance(processInfoType, nonPublic: true);
                processInfoType.GetField("processName", BindingFlags.Public | BindingFlags.Instance)
                    .SetValue(processInfo, name);

                typeof(Process).GetField("processInfo", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(process, processInfo);
                typeof(Process).GetField("processId", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(process, id);
                typeof(Process).GetField("haveProcessId", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(process, true);

                return process;
            }
        }

        [WindowsOnly]
        public class When_populating_process_tree_with_no_cycle : ProcessInformationSpecsBase
        {
            public ProcessTree Result;

            public override void Context()
            {
            }

            public override void Because()
            {
                var processA = CreateProcessWithId(1, "processA");
                var processB = CreateProcessWithId(2, "processB");
                var processC = CreateProcessWithId(3, "processC");

                Func<Process, Process> getParent = p =>
                {
                    if (p.Id == 1) return processB;
                    if (p.Id == 2) return processC;
                    return null;
                };

                Result = ProcessInformation.PopulateProcessTree(
                    new ProcessTree("processA"), processA, getParent);
            }

            [Fact]
            public void Should_contain_all_parent_processes()
            {
                Result.Processes.Count.Should().Be(2);
            }

            [Fact]
            public void Should_have_processB_as_first_parent()
            {
                Result.Processes.First.Value.Should().Be("processB");
            }

            [Fact]
            public void Should_have_processC_as_last_parent()
            {
                Result.Processes.Last.Value.Should().Be("processC");
            }
        }

        [WindowsOnly]
        public class When_populating_process_tree_with_a_cycle : ProcessInformationSpecsBase
        {
            public ProcessTree Result;

            public override void Context()
            {
            }

            public override void Because()
            {
                var processA = CreateProcessWithId(1, "processA");
                var processB = CreateProcessWithId(2, "processB");
                var processC = CreateProcessWithId(3, "processC");

                Func<Process, Process> getParent = p =>
                {
                    if (p.Id == 1) return processB;
                    if (p.Id == 2) return processC;
                    if (p.Id == 3) return processA;
                    return null;
                };

                Result = ProcessInformation.PopulateProcessTree(
                    new ProcessTree("processA"), processA, getParent);
            }

            [Fact]
            public void Should_not_loop_indefinitely()
            {
                Result.Processes.Count.Should().BeLessOrEqualTo(3);
            }

            [Fact]
            public void Should_stop_before_revisiting_a_pid()
            {
                Result.Processes.Count.Should().Be(2);
            }
        }

        [WindowsOnly]
        public class When_populating_process_tree_with_access_denied : ProcessInformationSpecsBase
        {
            public ProcessTree Result;

            public override void Context()
            {
            }

            public override void Because()
            {
                var processA = CreateProcessWithId(1, "processA");
                var processB = CreateProcessWithId(2, "processB");

                Func<Process, Process> getParent = p =>
                {
                    if (p.Id == 1) return processB;
                    if (p.Id == 2) throw new Win32Exception(5);
                    return null;
                };

                Result = ProcessInformation.PopulateProcessTree(
                    new ProcessTree("processA"), processA, getParent);
            }

            [Fact]
            public void Should_catch_access_denied_and_continue()
            {
                Result.Processes.Count.Should().Be(1);
            }

            [Fact]
            public void Should_have_added_process_before_exception()
            {
                Result.Processes.First.Value.Should().Be("processB");
            }
        }

        [WindowsOnly]
        public class When_populating_process_tree_with_win32_exception : ProcessInformationSpecsBase
        {
            public Win32Exception ResultException;

            public override void Context()
            {
            }

            public override void Because()
            {
                var processA = CreateProcessWithId(1, "processA");
                var processB = CreateProcessWithId(2, "processB");

                Func<Process, Process> getParent = p =>
                {
                    if (p.Id == 1) return processB;
                    if (p.Id == 2) throw new Win32Exception(42);
                    return null;
                };

                Action act = () => ProcessInformation.PopulateProcessTree(
                    new ProcessTree("processA"), processA, getParent);

                ResultException = act.Should().Throw<Win32Exception>().Which;
            }

            [Fact]
            public void Should_rethrow_the_exception()
            {
                ResultException.NativeErrorCode.Should().Be(42);
            }
        }
    }
}
