﻿namespace dotless.Core.Parser.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using Functions;
    using Nodes;
    using Tree;

    public class Env
    {
        private readonly Dictionary<string, Type> _functionTypes;
        private readonly Dictionary<int, IExtension> _extensions;

        public Stack<Ruleset> Frames { get; protected set; }
        public bool Compress { get; set; }
        public Node Rule { get; set; }
        public Output Output { get; private set; }

        public Env()
        {
            Frames = new Stack<Ruleset>();
            Output = new Output(this);

            _extensions = new Dictionary<int, IExtension>();
            _functionTypes = new Dictionary<string, Type>();

            AddCoreFunctions();
        }

        protected Env(Stack<Ruleset> frames, Dictionary<string, Type> functions, Dictionary<int, IExtension> extensions)
        {
            Frames = frames;
            Output = new Output(this);

            _extensions = extensions ?? new Dictionary<int, IExtension>();
            _functionTypes = functions ?? new Dictionary<string, Type>();

            if (_functionTypes.Count == 0)
                AddCoreFunctions();
        }

        /// <summary>
        ///  Creates a new Env variable for the purposes of scope
        /// </summary>
        public virtual Env CreateChildEnv(Stack<Ruleset> frames)
        {
            return new Env(frames, _functionTypes, _extensions);
        }

        /// <summary>
        ///  Adds an extension to this Env to be used whenever this Env is used
        /// </summary>
        public void AddExension(IExtension extension)
        {
            if (extension == null) throw new ArgumentNullException("extension");

            var hashCode = extension.GetType().GetHashCode();

            if (_extensions.ContainsKey(hashCode))
            {
                string message = String.Format("Extension type is already loaded: {0}", extension.GetType().FullName);
                throw new InvalidOperationException(message);
            }

            _extensions.Add(hashCode, extension);
            extension.Setup(this);
        }

        /// <summary>
        ///  Returns an extension of this type (if loaded). Otherwise will return null.
        /// </summary>
        public T1 GetExtension<T1>() where T1 : IExtension
        {
            return (T1)_extensions[typeof(T1).GetHashCode()];
        }

        /// <summary>
        ///  Finds the first scoped variable with this name
        /// </summary>
        public Rule FindVariable(string name)
        {
            return FindVariable(name, Rule);
        }

        /// <summary>
        ///  Finds the first scoped variable matching the name, using Rule as the current rule to work backwards from
        /// </summary>
        public Rule FindVariable(string name, Node rule)
        {
            var previousNode = rule;
            foreach (var frame in Frames)
            {
                var v = frame.Variable(name, previousNode);
                if (v)
                    return v;
                previousNode = frame;
            }
            return null;
        }

        /// <summary>
        ///  Finds the first Ruleset matching the selector argument
        /// </summary>
        public IEnumerable<Closure> FindRulesets(Selector selector)
        {
            return Frames.Select(frame => frame.Find(this, selector, null))
                .Select(matchedClosuresList => matchedClosuresList.Where(
                            matchedClosure => !Frames.Any(frame => frame.IsEqualOrClonedFrom(matchedClosure.Ruleset))))
                .FirstOrDefault(matchedClosuresList => matchedClosuresList.Count() != 0);
        }

        /// <summary>
        ///  Adds a Function to this Env object
        /// </summary>
        public void AddFunction(string name, Type type)
        {
            if (name == null) throw new ArgumentNullException("name");
            if (type == null) throw new ArgumentNullException("type");

            _functionTypes[name] = type;
        }

        /// <summary>
        ///  Given an assembly, adds all the dotless Functions in that assembly into this Env.
        /// </summary>
        public void AddFunctionsFromAssembly(Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException("assembly");

            var functionType = typeof (Function);

            foreach (var func in assembly
                .GetTypes()
                .Where(t => functionType.IsAssignableFrom(t) && t != functionType)
                .Where(t => !t.IsAbstract)
                .SelectMany(GetFunctionNames))
            {
                AddFunction(func.Key, func.Value);
            }
        }

        private void AddCoreFunctions()
        {
            AddFunctionsFromAssembly(Assembly.GetExecutingAssembly());
            AddFunction("%", typeof (CFormatString));
        }

        /// <summary>
        ///  Given a function name, returns a new Function matching that name.
        /// </summary>
        public virtual Function GetFunction(string name)
        {
            name = name.ToLowerInvariant();

            if (_functionTypes.ContainsKey(name))
                return (Function) Activator.CreateInstance(_functionTypes[name]);

            return null;
        }

        private static IEnumerable<KeyValuePair<string, Type>> GetFunctionNames(Type t)
        {
            var name = t.Name;

            if (name.EndsWith("function", StringComparison.InvariantCultureIgnoreCase))
                name = name.Substring(0, name.Length - 8);

            name = Regex.Replace(name, @"\B[A-Z]", "-$0");

            name = name.ToLowerInvariant();

            yield return new KeyValuePair<string, Type>(name, t);

            if(name.Contains("-"))
                yield return new KeyValuePair<string, Type>(name.Replace("-", ""), t);
        }
    }
}