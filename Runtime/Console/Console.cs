// smidgens @ github

namespace Smidgenomics.Unity.Console
{
	using System;
	using System.Collections.Generic;
	using System.Reflection;
	using System.Text;
	using UnityEngine;

	internal sealed class Console : IConsole
	{
		public static class LogMsg
		{
			public const string NO_RESULTS = "No results";
			public const string VARIABLES = "<b>Variables:</b>";
			public const string METHODS = "<b>Functions:</b>";
			public const string UNKNOWN_CMD = "Unknown function: '{0}'";
			public const string UNKNOWN_ERR = "Unknown error in handler";
			public const string NOT_IMPLEMENTED = "Not implemented";
			public const string VAR_NF = "Variable not found: '{0}'";
			public const string CMD_NF = "Function not found: ";
		}

		internal static class Keyword
		{
			public const string LIST = "list";
			public const string HELP = "describe";
			public const string INSPECT = "inspect";
			public const string RUN_SCRIPT = "exec_file";
			public const string CLEAR = "clear";
		}

		public IConsoleLog Log => _log;

		internal void ClearCommands()
		{
			_nextKey = 1;
			foreach (var (k, h) in _handlers)
			{
				h.handle.Invalidate();
			}
			_handlers.Clear();
		}

		private void Clear()
		{
			_log.ClearLog();
		}

		public void Exec(string input)
		{
			try
			{
				var r = ConsoleParse.Command(input);
				Run(input, r);
			}
			catch (ConsoleException e)
			{
				var err = $"Invalid input: [{input}] \n  " + e.Message;
				_log.Append(err, ELogType.Error);
			}
		}

		private MemberInfoValidator FindValidator(Type type)
		{
			foreach (var (vType, v) in _BIND_VALIDATORS)
			{
				if (vType.IsAssignableFrom(type))
				{
					return v;
				}
			}
			return null;
		}

		public CommandHandle Bind
		(
			string name,
			MemberInfo mi,
			object context,
			string description = null
		)
		{
			description ??= string.Empty;

			var validator = FindValidator(mi.GetType());

			if (validator == null)
			{
				throw new ConsoleBindingException("Member type not supported in console");
			}

			var ex = validator.Invoke(mi, context);

			if (ex != null)
			{
				throw ex;
			}

			var handle = CreateCommandHandle();

			CHandler handler = mi is MethodInfo
			? new MethodHandler(handle, name, description, mi, context)
			: new VariableHandler(handle, name, description, mi, context);
			
			_handlers[handle.Key] = handler;

			return handle;
		}

		private readonly Dictionary<ulong, CHandler> _handlers = new();
		private readonly ConsoleLog _log = new ();
		private ulong _nextKey = 1;

		private CommandHandle CreateCommandHandle()
		{
			return new CommandHandle(_nextKey++, UnbindHandle);
		}

		private delegate ConsoleException MemberInfoValidator(MemberInfo m, object ctx);

		private static readonly Dictionary<Type, MemberInfoValidator> _BIND_VALIDATORS = new()
		{
			// METHOD
			{typeof(MethodInfo), (mi, ctx) =>
			{
				var m = (mi as MethodInfo)!;
				if (m.ReturnType != typeof(void))
				{
					return new ConsoleBindingException($"Cannot bind method with return value: '{m.Name}'");
				}

				if (m.IsStatic && ctx != null)
				{
					return new ConsoleBindingException("Cannot bind static method with context");
				}

				if (!m.IsStatic && ctx == null)
				{
					return new ConsoleBindingException("Cannot bind instance method without context");
				}

				if (!m.IsStatic && ctx?.GetType() != m.ReflectedType)
				{
					return new ConsoleBindingException("Cannot bind method with context of different type");
				}

				var parameters = m.GetParameters();
				foreach (var p in parameters)
				{
					if (!ConsoleHelper.IsConsoleUsable(p))
					{
						return new ConsoleException($"Invalid method parameter type: {p.ParameterType.Name}");
					}
				}
				
				return null;
			}},
			// FIELD
			{typeof(FieldInfo), (mi, ctx) =>
			{
				var f = (mi as FieldInfo)!;
				if (f.IsInitOnly)
				{
					return new ConsoleBindingException("Static field must support read/write");
				}

				if (f.IsStatic && ctx != null)
				{
					return new ConsoleBindingException("Cannot bind static field with context");
				}
				if (!f.IsStatic && ctx == null)
				{
					return new ConsoleBindingException("Cannot bind instance field without context");
				}
				if (!ConsoleHelper.IsConsoleUsable(f.FieldType))
				{
					throw new ConsoleException();
				}
				return null;
			}},
			// PROPERTY
			{typeof(PropertyInfo), (mi, ctx) =>
			{
				var p = (mi as PropertyInfo)!;
				if (!ConsoleHelper.IsConsoleUsable(p.PropertyType))
				{
					return new ConsoleBindingException("Property type not supported");
				}

				if (!p.IsReadWrite())
				{
					return new ConsoleBindingException("Bound property must support read/write");
				}
				var setter = p.SetMethod;

				if (setter.IsStatic && ctx != null)
				{
					return new ConsoleBindingException("Cannot bind static property with context");
				}
				if (!setter.IsStatic && ctx == null)
				{
					return new ConsoleBindingException("Cannot bind instance property without context");
				}

				if (!setter.IsStatic && ctx?.GetType() != setter.ReflectedType)
				{
					return new ConsoleBindingException("Cannot bind property with context of different type");
				}
				return null;
			}},
		};

		private abstract class CHandler
		{
			public readonly CommandHandle handle;
			public readonly string name, description;
			public readonly MemberInfo member;
			public readonly object ctx;
			private string _stringifiedSignature;
			protected abstract void StringifySignature(StringBuilder b);

			public abstract void Invoke(in CommandRequest req);

			public abstract bool MatchCallSignature(in CommandRequest req);

			public string GetStringifiedSignature()
			{
				if (_stringifiedSignature == null)
				{
					var sb = new StringBuilder();
					StringifySignature(sb);
					_stringifiedSignature = sb.ToString();
				}
				return _stringifiedSignature;
			}

			protected CHandler(CommandHandle h, string name, string descr, MemberInfo member, object ctx)
			{
				handle = h;
				this.name = name;
				this.description = descr;
				this.member = member;
				this.ctx = ctx;
			}
		}

		private sealed class MethodHandler : CHandler
		{
			internal MethodHandler(CommandHandle h, string name, string descr, MemberInfo member, object ctx)
			: base(h, name, descr, member, ctx)
			{
				_method = (member as MethodInfo)!;
				_parameters = _method.GetParameters();

				if (_method.ReturnType == typeof(void) && _parameters.Length == 0)
				{
					_delegateFn = (Action)_method.CreateDelegate(typeof(Action), ctx);
					_invokeFn = InvokeAction;
				}
				else
				{
					_invokeFn = InvokeReflection;
				}
			}

			private readonly MethodInfo _method;
			private readonly ParameterInfo[] _parameters;
			private delegate void InvokeCallback(in CommandRequest r);
			private readonly InvokeCallback _invokeFn;
			private readonly Action _delegateFn;

			protected override void StringifySignature(StringBuilder s)
			{
				s.Append(name);
				s.Append(" (");
				var pi = 0;
				foreach (var p in _parameters)
				{
					s.Append("<color=orange>");
					s.Append("<i>");
					s.Append(p.ParameterType.GetNameOrAlias());
					s.Append("</i>");
					s.Append("</color>");
					if (pi < _parameters.Length - 1)
					{
						s.Append(", ");
					}
					pi++;
				}
				s.Append(")");
			}

			public override void Invoke(in CommandRequest req)
			{
				_invokeFn.Invoke(req);
			}
			
			private void InvokeReflection(in CommandRequest req)
			{
				_method.Invoke(ctx, req.args);
			}

			// optimization for basic delegate
			private void InvokeAction(in CommandRequest req)
			{
				_delegateFn.Invoke();
			}

			public override bool MatchCallSignature(in CommandRequest r)
			{
				if (r.type != ECommandType.MethodCall)
				{
					return false;
				}
				if (_method == null)
				{
					return false;
				}

				if (name != r.keyword)
				{
					return false;
				}

				if (_parameters.Length != r.args.Length)
				{
					return false;
				}
				for (var i = 0; i < r.args.Length; i++)
				{
					var vtype = r.args[i].GetType();
					var ptype = _parameters[i].ParameterType;
					if (vtype != ptype) { return false; }
				}
				return true;
			}
		}

		private sealed class VariableHandler : CHandler
		{
			internal VariableHandler(CommandHandle h, string name, string descr, MemberInfo member, object ctx)
				: base(h, name, descr, member, ctx)
			{
				if (member is PropertyInfo pi)
				{
					_variableType = pi.PropertyType;
				}
				else if (member is FieldInfo fi)
				{
					_variableType = fi.FieldType;
				}
			}

			private readonly Type _variableType;

			public object ReadValue()
			{
				if (member is PropertyInfo pi)
				{
					return pi.GetValue(ctx);
				}
				else if (member is FieldInfo fi)
				{
					return fi.GetValue(ctx);;
				}
				return null;
			}

			protected override void StringifySignature(StringBuilder s)
			{
				s.Append(name);
				s.Append(":");
				s.Append("<color=orange>");
				s.Append("<i>");
				s.Append(_variableType.GetNameOrAlias());
				s.Append("</i>");
				s.Append("</color>");
			}

			public override void Invoke(in CommandRequest req)
			{
				if (member is PropertyInfo pi)
				{
					pi.SetValue(ctx, req.args[0]);
				}
				else if (member is FieldInfo fi)
				{
					fi.SetValue(ctx, req.args[0]);
				}
			}

			public override bool MatchCallSignature(in CommandRequest r)
			{
				if (r.type != ECommandType.Assignment)
				{
					return false;
				}
				if (_variableType != r.args.FirstOrDefault()?.GetType())
				{
					return false;
				}
				if (name != r.keyword)
				{
					return false;
				}
				return true;
			}
		}

		internal void FindAssemblyCommands()
		{
			foreach(var cc in ConsoleHelper.FindConsoleCallables())
			{
				Bind(cc.keyword, cc.member, null, cc.description);
			}
		}

		internal void InitDefaultCommands(EDefaultConsoleCommand flags)
		{
			if (flags.HasFlag(EDefaultConsoleCommand.List))
			{
				Bind(Keyword.LIST, GetMethod(ListHandlers), this, "Lists all commands");
			}

			if (flags.HasFlag(EDefaultConsoleCommand.Clear))
			{
				Bind(Keyword.CLEAR, GetMethod(Clear), this, "Clears console");
			}

			if (flags.HasFlag(EDefaultConsoleCommand.Describe))
			{
				Bind(Keyword.HELP, GetMethod<string>(DescribeCommand), this, "Show command description");
			}

			if (flags.HasFlag(EDefaultConsoleCommand.Inspect))
			{
				Bind(Keyword.INSPECT, GetMethod<string>(InspectVariable), this, "Show value of variable");
			}

			// if (flags.HasFlag(EDefaultConsoleCommand.Exec))
			// {
			// 	Bind(Keyword.RUN_SCRIPT, GetMethod<string>(RunScript), this, "Run file");
			// }
		}

		private static MethodInfo GetMethod(Action a) => a.Method;
		private static MethodInfo GetMethod<T>(Action<T> a) => a.Method;

		private void UnbindHandle(ulong key)
		{
			_handlers.Remove(key);
		}

		private void LogNF(in CommandRequest r)
		{
			switch (r.type)
			{
				case ECommandType.Assignment:
					LogVariableNF(r.keyword);
					break;
				case ECommandType.MethodCall:
					LogMethodNF(r.keyword, r.args);
					break;
			}
		}

		private CHandler FindHandler(in CommandRequest r)
		{
			foreach (var (k, h) in _handlers)
			{
				if (h.MatchCallSignature(r))
				{
					return h;
				}
			}
			return null;
		}

		private void Run(string rawInput, in CommandRequest r)
		{
			var h = FindHandler(r);
			
			if(h == null)
			{
				LogNF(r);
				return;
			}

			this.LogExpression(rawInput);

			try
			{
				h.Invoke(r);
			}
			catch(Exception e)
			{
#if SM_DEV
				Debug.Log(e.Message);
#endif
				LogUnknownError();
			}
		}

		private void LogVariableNF(string name) => this.LogWarning(string.Format(LogMsg.VAR_NF, name));

		private void LogUnknownError() => this.LogError(LogMsg.UNKNOWN_ERR);

		private void LogMethodNF(string name, object[] args)
		{
			var sb = new StringBuilder();
			sb.Append(LogMsg.CMD_NF);
			sb.Append(name);
			sb.Append(' ');
			sb.Append('(');
			if (args.Length == 0)
			{
				sb.Append("void");
			}
			
			for (var i = 0; i < args.Length; i++)
			{
				sb.Append(args[0].GetType().GetNameOrAlias());
				if (i < args.Length - 1)
				{
					sb.Append(',');
				}
			}
			sb.Append(')');
			this.LogWarning(sb.ToString());
		}

		private void InspectVariable(string name)
		{
			foreach (var (k, h) in _handlers)
			{
				if (h is VariableHandler vh && h.name == name)
				{
					try
					{
						this.LogMessage(vh.ReadValue()?.ToString());
					}
					catch
					{
						// ignored
					}
					return;
				}
			}
			LogVariableNF(name);
		}

		private void RunScript(string path)
		{
			this.LogWarning(LogMsg.NOT_IMPLEMENTED);
		}

		private void DescribeCommand(string name)
		{
			foreach (var (k, h) in _handlers)
			{
				if (h.name == name)
				{
					this.LogMessage(h.description);
					return;
				}
			}
			this.LogWarning(string.Format(LogMsg.UNKNOWN_CMD, name));
		}

		public void ListHandlers()
		{
			var s = new StringBuilder();
			StringifyHandlers(s);
			_log.Append(s.ToString(), ELogType.Info);
		}

		private void StringifyHandlers(StringBuilder s)
		{
			var vs = new StringBuilder();

			var indent = "  > ";
			vs.AppendLine(LogMsg.VARIABLES);
			var vcount = 0;
			foreach (var (k, h) in _handlers)
			{
				if (h is VariableHandler)
				{
					vcount++;
					vs.Append(indent);
					vs.Append(h.GetStringifiedSignature());
					vs.Append('\n');
				}
			}

			if(vcount > 0)
			{
				s.Append(vs);
			}

			var ms = new StringBuilder();
			ms.AppendLine(LogMsg.METHODS);
			var mcount = 0;
			foreach (var (k, h) in _handlers)
			{
				if (h is MethodHandler)
				{
					mcount++;
					ms.Append(indent);
					ms.Append(h.GetStringifiedSignature());
					ms.Append('\n');
				}
			}
			if (mcount > 0)
			{
				s.Append(ms);
			}

			if(mcount == 0 && vcount == 0)
			{
				s.AppendLine(LogMsg.NO_RESULTS);
			}
		}
	}
}