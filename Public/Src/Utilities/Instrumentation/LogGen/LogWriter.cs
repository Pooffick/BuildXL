// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BuildXL.Utilities.CodeGenerationHelper;
using Microsoft.CodeAnalysis;
using EventGenerators = BuildXL.Utilities.Instrumentation.Common.Generators;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.LogGen
{
    /// <summary>
    /// Writes the generated loggers to implement the partial class
    /// </summary>
    internal sealed class LogWriter
    {
        private const string GlobalInstrumentationNamespace = "global::BuildXL.Utilities.Instrumentation.Common";
        private const string NotifyContextWhenErrorsAreLogged = "m_notifyContextWhenErrorsAreLogged";
        private const string NotifyContextWhenWarningsAreLogged = "m_notifyContextWhenWarningsAreLogged";

        private readonly string m_path;
        private readonly string m_namespace;
        private readonly string m_targetFramework;
        private readonly string m_targetRuntime;
        private readonly ErrorReport m_errorReport;

        private List<GeneratorBase> m_generators;

        /// <nodoc />
        public LogWriter(Configuration config, ErrorReport errorReport)
        {
            m_path = config.OutputCSharpFile;
            m_namespace = config.Namespace;
            m_errorReport = errorReport;
            m_targetFramework = config.TargetFramework;
            m_targetRuntime = config.TargetRuntime;
        }

        /// <summary>
        /// Writes the log file
        /// </summary>
        public int WriteLog(IReadOnlyList<LoggingClass> loggingClasses)
        {
            var itemsWritten = 0;
            using (var fs = File.Open(m_path, FileMode.Create))
            using (StreamWriter writer = new StreamWriter(fs))
            {
                CodeGenerator gen = new CodeGenerator((c) => writer.Write(c));
                gen.Ln("// <auto-generated/>\r\n");
                gen.Ln();
                gen.Ln("#nullable enable");
                gen.Ln();

                Dictionary<LoggingSite, List<GeneratorBase>> generatorMap = CreateGenerators(loggingClasses, gen);

                foreach (var loggingClass in loggingClasses)
                {
                    itemsWritten++;
                    gen.Ln("namespace {0}", loggingClass.Symbol.ContainingNamespace);
                    using (gen.Br)
                    {
                        // Figure out the namespaces needed
                        IDictionary<string, HashSet<string>> namespacesWithConditions = CombineNamespaces();
                        foreach (var namespacesWithCondition in namespacesWithConditions)
                        {
                            bool hasCondition = !string.IsNullOrEmpty(namespacesWithCondition.Key);
                            if (hasCondition)
                            {
                                gen.Ln("#if {0}", namespacesWithCondition.Key);
                            }
                            else
                            {
                                namespacesWithCondition.Value.Add(m_namespace);
                            }

                            foreach (var ns in namespacesWithCondition.Value)
                            {
                                gen.Ln("using {0};", ns);
                            }

                            if (hasCondition)
                            {
                                gen.Ln("#endif");
                            }
                        }

                        gen.Ln();
                        gen.Ln("#pragma warning disable 219");
                        gen.Ln();

                        GenerateInterface(gen, loggingClass);
                        GenerateLoggerInstance(gen, loggingClass, generatorMap);
                        GenerateImplementations(gen, loggingClass, generatorMap);
                    }
                }

                foreach (GeneratorBase generator in m_generators)
                {
                    gen.Ln();
                    generator.GenerateClass();
                }
            }

            return itemsWritten;
        }

        private void GenerateInterface(CodeGenerator gen, LoggingClass loggingClass)
        {
            gen.GenerateSummaryComment("Logger interface");
            gen.WriteGeneratedAttribute(includeCodeCoverageExclusion: false);
            gen.Ln($"{GetAccessibilityString(loggingClass.Symbol.DeclaredAccessibility)} interface I{loggingClass.Name} : {GlobalInstrumentationNamespace}.ILogger");
            using (gen.Br)
            {
                // Log methods
                foreach (var site in loggingClass.Sites)
                {
                    gen.GenerateSummaryComment($"({site.Level}) - {new XText(site.SpecifiedMessageFormat).ToString()}");
                    gen.Ln($"void {site.Method.Name}( {GenerateParameterString(site, includeContext: true)});");
                    gen.Ln();
                }
            }
        }

        private void GenerateLoggerInstance(CodeGenerator gen, LoggingClass loggerClass, Dictionary<LoggingSite, List<GeneratorBase>> generatorMap)
        {
            var className = "Log";
            gen.GenerateSummaryComment("Instance based logger");
            gen.WriteGeneratedAttribute();
            gen.Ln($"{GetAccessibilityString(loggerClass.Symbol.DeclaredAccessibility)} class {className}");
            using (gen.Br)
            {
                gen.GenerateSummaryComment("The static logger this delegates to");
                gen.Ln($"private {loggerClass.Symbol.Name} m_logger;");
                gen.Ln();

                gen.GenerateSummaryComment("The logging context to use.");
                gen.Ln($"public {GlobalInstrumentationNamespace}.LoggingContext LoggingContext {{ get; }}");
                gen.Ln();

                gen.GenerateSummaryComment("Creates a new instnce base logger.");
                gen.Ln($"public Log({GlobalInstrumentationNamespace}.LoggingContext loggingContext, bool preserveLogEvents = false)");
                using (gen.Br)
                {
                    gen.Ln($"m_logger = {loggerClass.Symbol.Name}.CreateLogger(preserveLogEvents);");
                    gen.Ln("LoggingContext = loggingContext;");
                }

                foreach (LoggingSite site in loggerClass.Sites)
                {
                    List<GeneratorBase> generators = generatorMap[site];

                    gen.GenerateSummaryComment("Logging implementation");

                    gen.Ln($"{GetAccessibilityString(site.Method.DeclaredAccessibility)} void {site.Method.Name}({GenerateParameterString(site, includeContext: true)})");
                    using (gen.Br)
                    {
                        gen.Ln($"m_logger.{site.Method.Name}({GenerateArgumentString(site, includeContext: true)});");
                    }
                    gen.Ln();
                }
            }
        }

        private void GenerateImplementations(CodeGenerator gen, LoggingClass loggingClass, Dictionary<LoggingSite, List<GeneratorBase>> generatorMap)
        {
            var symbol = loggingClass.Symbol;
            gen.GenerateSummaryComment("Logging Instantiation");
            gen.WriteGeneratedAttribute();
            gen.Ln(
                "{0} partial class {1} : {2}.LoggerBase",
                GetAccessibilityString(symbol.DeclaredAccessibility),
                symbol.Name,
                GlobalInstrumentationNamespace);
            using (gen.Br)
            {
                gen.Ln("static private Logger m_log = new {0}Impl();", symbol.Name);
                gen.Ln();

                gen.GenerateSummaryComment("Factory method that creates instances of the logger.");
                gen.Ln($"public static {symbol.Name} CreateLogger(bool preserveLogEvents = false)");
                using (gen.Br)
                {
                    gen.Ln($"return new {symbol.Name}Impl");
                    using (gen.Br)
                    {
                        gen.Ln("PreserveLogEvents = preserveLogEvents,");
                        gen.Ln("InspectMessageEnabled = preserveLogEvents,");
                    }
                    gen.Ln(";");
                }
                gen.Ln();

                gen.GenerateSummaryComment("Factory method that creates instances of the logger that tracks errors and allows for observers");
                gen.Ln($"public static {symbol.Name} CreateLoggerWithTracking(bool preserveLogEvents = false)");
                using (gen.Br)
                {
                    gen.Ln($"return new {symbol.Name}Impl");
                    using (gen.Br)
                    {
                        gen.Ln("PreserveLogEvents = preserveLogEvents,");
                        gen.Ln("InspectMessageEnabled = true,");
                    }
                    gen.Ln(";");
                }
                gen.Ln();

                var notifyContextWhenErrorsAreLoggedIsUsed = false;
                var notifyContextWhenWarningsAreLoggedIsUsed = false;

                foreach (GeneratorBase generator in m_generators)
                {
                    generator.GenerateAdditionalLoggerMembers();
                }

                gen.GenerateSummaryComment("Logging implementation");
                gen.WriteGeneratedAttribute();
                gen.Ln("private class {0}Impl: {0}", symbol.Name);
                using (gen.Br)
                {
                    foreach (LoggingSite site in loggingClass.Sites)
                    {
                        List<GeneratorBase> generators = generatorMap[site];

                        gen.GenerateSummaryComment("Logging implementation");
                        
                        var parametersWithContext = GenerateParameterString(site, includeContext: true);

                        gen.Ln(I($"{GetAccessibilityString(site.Method.DeclaredAccessibility)} override void {site.Method.Name}({parametersWithContext})"));
                        using (gen.Br)
                        {
                            var argsWithContext = GenerateArgumentString(site, includeContext: true);
                            var argsWithoutContext = GenerateArgumentString(site, includeContext: true);

                            if (site.Level == BuildXL.Utilities.Instrumentation.Common.Level.Critical)
                            {
                                // Critical events are always logged synchronously
                                gen.Ln(I($"{site.Method.Name}_Core({argsWithContext});"));
                            }
                            else
                            {
                                gen.Ln(I($"if ({site.LoggingContextParameterName}.{nameof(BuildXL.Utilities.Instrumentation.Common.LoggingContext.IsAsyncLoggingEnabled)})"));
                                using (gen.Br)
                                {
                                    // NOTE: This allocates a closure for every log message when async logging is enabled.
                                    // This is assumed to not be non-issue as the logging infrastructure already has many allocations
                                    // as a part of logging so this allocation doesn't 
                                    gen.Ln(I($"EnqueueLogAction({site.LoggingContextParameterName}, {site.Id}, () => {site.Method.Name}_Core({argsWithContext}));"));
                                }
                                gen.Ln("else");
                                using (gen.Br)
                                {
                                    gen.Ln(I($"{site.Method.Name}_Core({argsWithContext});"));
                                }
                            }

                            // Register errors on the logging context so code can assert that errors were logged
                            if (site.Level == BuildXL.Utilities.Instrumentation.Common.Level.Error)
                            {
                                notifyContextWhenErrorsAreLoggedIsUsed = true;
                                gen.Ln(I($"if ({NotifyContextWhenErrorsAreLogged})"));
                                using (gen.Br)
                                {
                                    gen.Ln(I($"{site.LoggingContextParameterName}.SpecifyErrorWasLogged({site.Id});"));
                                }
                            }

                            // Register warnings on the logging context so code can assert that warnings were logged
                            if (site.Level == BuildXL.Utilities.Instrumentation.Common.Level.Warning)
                            {
                                notifyContextWhenWarningsAreLoggedIsUsed = true;
                                gen.Ln(I($"if ({NotifyContextWhenWarningsAreLogged})"));
                                using (gen.Br)
                                {
                                    gen.Ln(I($"{site.LoggingContextParameterName}.SpecifyWarningWasLogged();"));
                                }
                            }
                        }

                        gen.Ln();
                        gen.Ln(I($"private void {site.Method.Name}_Core({parametersWithContext})"));
                        using (gen.Br)
                        {
                            List<char> interceptedCode = new List<char>();
                            using (gen.InterceptOutput((c) => interceptedCode.Add(c)))
                            {
                                foreach (GeneratorBase generator in generators)
                                {
                                    generator.GenerateLogMethodBody(
                                        site,
                                        getMessageExpression: () =>
                                                              {
                                                                  // Track whether the getMessage() function was called where there is a format string
                                                                  if (site.GetMessageFormatParameters().Any())
                                                                  {
                                                                      // Only InspecMessage takes a fully constructed message.
                                                                      // To avoid redundant allocations this callback creates
                                                                      // an argument instead of creating a local variable.
                                                                      return
                                                                          string.Format(
                                                                              CultureInfo.InvariantCulture,
                                                                              "string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{0}\", {1})",
                                                                              site.GetNormalizedMessageFormat(),
                                                                              string.Join(", ", site.GetMessageFormatParameters()));
                                                                  }

                                                                  return "\"" + site.SpecifiedMessageFormat + "\"";
                                                              });
                                }
                            }

                            // Now we can write out the intercepted code from the code generators
                            foreach (char c in interceptedCode)
                            {
                                gen.Output(c);
                            }
                        }

                        gen.Ln();
                    }
                }

                if (notifyContextWhenErrorsAreLoggedIsUsed)
                {
                    gen.Ln(I($"private bool {NotifyContextWhenErrorsAreLogged} = true;"));
                }

                if (notifyContextWhenWarningsAreLoggedIsUsed)
                {
                    gen.Ln(I($"private bool {NotifyContextWhenWarningsAreLogged} = true;"));
                }
            }
        }

        public static string GenerateParameterString(LoggingSite site, bool includeContext)
        {
            var contextParameter = includeContext
                ? I($"{GlobalInstrumentationNamespace}.LoggingContext {site.LoggingContextParameterName}") + (site.Payload.Any() ? ", " : string.Empty)
                : string.Empty;

            return contextParameter + string.Join(", ", site.Payload.Select(
                parameter =>
                {
                    var modifier = parameter.RefKind == RefKind.Ref ? "ref" : string.Empty;
                    var typeValue = parameter.Type.ToDisplayString();
                    if (parameter.Type.SpecialType == SpecialType.System_String && parameter.HasExplicitDefaultValue && parameter.ExplicitDefaultValue == null)
                    {
                        typeValue += "?";
                    }
                    var defaultValue = parameter.HasExplicitDefaultValue 
                        ? " = " + (parameter.ExplicitDefaultValue == null ? "null" : parameter.ExplicitDefaultValue.ToString())
                        : string.Empty;
                    return $"{modifier}{typeValue} {parameter.Name}{defaultValue}";
                })
            );
        }

        public static string GenerateArgumentString(LoggingSite site, bool includeContext)
        {
             var contextArgument = includeContext
                ? site.LoggingContextParameterName + (site.Payload.Any() ? ", " : string.Empty)
                : string.Empty;

            return contextArgument + string.Join(", ", site.Payload.Select(
                parameter =>
                {
                    var modifier = parameter.RefKind == RefKind.Ref ? "ref" : string.Empty;
                    return $"{modifier} {parameter.Name}";
                })
            );
        }

        /// <summary>
        /// Creates a mapping of logging site to the generators that must run for it
        /// </summary>
        private Dictionary<LoggingSite, List<GeneratorBase>> CreateGenerators(IReadOnlyList<LoggingClass> loggingClasses, CodeGenerator codeGenerator)
        {
            Dictionary<EventGenerators, GeneratorBase> generatorsByName = new Dictionary<EventGenerators, GeneratorBase>();
            Dictionary<LoggingSite, List<GeneratorBase>> generatorsBySite = new Dictionary<LoggingSite, List<GeneratorBase>>();

            foreach (var loggingClass in loggingClasses)
            {
                foreach (var site in loggingClass.Sites)
                {
                    List<GeneratorBase> generators = new List<GeneratorBase>();

                    foreach (EventGenerators gen in Enum.GetValues(typeof(EventGenerators)))
                    {
                        if (gen == EventGenerators.None)
                        {
                            continue;
                        }

                        if ((site.EventGenerators & gen) != 0)
                        {
                            GeneratorBase generator;
                            if (generatorsByName.TryGetValue(gen, out generator))
                            {
                                generators.Add(generator);
                            }
                            else
                            {
                                Func<GeneratorBase> generatorFactory;
                                if (!Parser.SupportedGenerators.TryGetValue(gen, out generatorFactory))
                                {
                                    // AriaV2Disabled is the only generator that's allow to be specified with not actual
                                    // generator existing
                                    if (gen != EventGenerators.AriaV2Disabled)
                                    {
                                        Contract.Assert(false, "Failed to find a generator for " + gen.ToString() +
                                            ". This should have been caught in Parsing");
                                    }
                                    continue;
                                }

                                generator = generatorFactory();
                                generator.Initialize(m_namespace, m_targetFramework, m_targetRuntime, codeGenerator, loggingClasses, m_errorReport);
                                generatorsByName.Add(gen, generator);
                                generators.Add(generator);
                            }
                        }
                    }

                    generatorsBySite[site] = generators;
                }
            }

            m_generators = new List<GeneratorBase>(generatorsByName.Values);

            return generatorsBySite;
        }

        private IDictionary<string, HashSet<string>> CombineNamespaces()
        {
            IDictionary<string, HashSet<string>> namespacesWithConditions = new Dictionary<string, HashSet<string>>();

            foreach (GeneratorBase generator in m_generators)
            {
                foreach (var ns in generator.ConsumedNamespaces)
                {
                    HashSet<string> namespaces;
                    if (!namespacesWithConditions.TryGetValue(ns.Item2, out namespaces))
                    {
                        namespaces = new HashSet<string>();
                        namespacesWithConditions[ns.Item2] = namespaces;
                    }

                    namespaces.Add(ns.Item1);
                }
            }

            return namespacesWithConditions;
        }

        private string GetAccessibilityString(Accessibility accessibility)
        {
            switch (accessibility)
            {
                case Accessibility.Private:
                    return "private";
                case Accessibility.Protected:
                    return "protected";
                case Accessibility.Internal:
                    return "internal";
                case Accessibility.Public:
                    return "public";
                default:
                    m_errorReport.ReportError("Unsupported accessibility type: {0}", accessibility);
                    return null;
            }
        }
    }
}
