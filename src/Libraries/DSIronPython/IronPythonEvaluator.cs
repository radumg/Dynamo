using System;
using System.Collections;
using System.Linq;
using Autodesk.DesignScript.Runtime;
using Dynamo.Utilities;
using IronPython.Hosting;

using Microsoft.Scripting.Hosting;
using System.IO;
using System.Text;

namespace DSIronPython
{
    [SupressImportIntoVM]
    public enum EvaluationState { Begin, Success, Failed }

    [SupressImportIntoVM]
    public delegate void EvaluationEventHandler(EvaluationState state,
                                                ScriptEngine engine,
                                                ScriptScope scope,
                                                string code,
                                                IList bindingValues);

    /// <summary>
    ///     Evaluates a Python script in the Dynamo context.
    /// </summary>
    [IsVisibleInDynamoLibrary(false)]
    public static class IronPythonEvaluator
    {
        /// <summary> stores a copy of the previously executed code</summary>
        private static string prev_code { get; set; }
        /// <summary> stores a copy of the previously compiled engine</summary>
        private static ScriptSource prev_script { get; set; }

        public static DebugWriter debugFS = new DebugWriter(@"C:\Users\radug\Desktop\testing\debugOutputStream.log");
       /// <summary>
       ///     Executes a Python script with custom variable names. Script may be a string
       ///     read from a file, for example. Pass a list of names (matching the variable
       ///     names in the script) to bindingNames and pass a corresponding list of values
       ///     to bindingValues.
       /// </summary>
       /// <param name="code">Python script as a string.</param>
       /// <param name="bindingNames">Names of values referenced in Python script.</param>
       /// <param name="bindingValues">Values referenced in Python script.</param>
        public static object EvaluateIronPythonScript(
            string code,
            IList bindingNames,
            [ArbitraryDimensionArrayImport] IList bindingValues)
        {
            if (code != prev_code)
            {
                ScriptSource script = Python.CreateEngine().CreateScriptSourceFromString(code);
                script.Compile();
                prev_script = script;
                prev_code = code;
            }

            ScriptEngine engine = prev_script.Engine;
            ScriptScope scope = engine.CreateScope();
            /*
            MemoryStream ms = new MemoryStream();
            var outputWr = new EventRaisingStreamWriter(ms);
            outputWr.StringWritten += new EventHandler<MyEvtArgs<string>>(sWr_StringWritten);

            engine.Runtime.IO.SetOutput(ms, System.Text.Encoding.UTF8);
            */
            engine.Runtime.IO.SetOutput(debugFS, Encoding.ASCII);

            //byte[] array = Encoding.ASCII.GetBytes(message);

            //DynamoModel.debugFS.Write(array, 0, array.Length);

            int amt = Math.Min(bindingNames.Count, bindingValues.Count);

            for (int i = 0; i < amt; i++)
            {
                scope.SetVariable((string)bindingNames[i], InputMarshaler.Marshal(bindingValues[i]));
            }

            try
            {
                OnEvaluationBegin(engine, scope, code, bindingValues);
                prev_script.Execute(scope);
            }
            catch (Exception e)
            {
                OnEvaluationEnd(false, engine, scope, code, bindingValues);
                var eo = engine.GetService<ExceptionOperations>();
                string error = eo.FormatException(e);
                throw new Exception(error);
            }

            OnEvaluationEnd(true, engine, scope, code, bindingValues);

            var result = scope.ContainsVariable("OUT") ? scope.GetVariable("OUT") : null;
            return OutputMarshaler.Marshal(result);
        }

        #region Marshalling

        /// <summary>
        ///     Data Marshaler for all data coming into a Python node.
        /// </summary>
        [SupressImportIntoVM]
        public static DataMarshaler InputMarshaler
        {
            get
            {
                if (inputMarshaler == null)
                {
                    inputMarshaler = new DataMarshaler();
                    inputMarshaler.RegisterMarshaler(
                        delegate(IList lst)
                        {
                            var pyList = new IronPython.Runtime.List();
                            foreach (var item in lst.Cast<object>().Select(inputMarshaler.Marshal))
                            {
                                pyList.Add(item);
                            }
                            return pyList;
                        });
                }
                return inputMarshaler;
            }
        }

        /// <summary>
        ///     Data Marshaler for all data coming out of a Python node.
        /// </summary>
        [SupressImportIntoVM]
        public static DataMarshaler OutputMarshaler
        {
            get { return outputMarshaler ?? (outputMarshaler = new DataMarshaler()); }
        }

        private static DataMarshaler inputMarshaler;
        private static DataMarshaler outputMarshaler;

        #endregion

        #region Evaluation events

        /// <summary>
        ///     Emitted immediately before execution begins
        /// </summary>
        [SupressImportIntoVM]
        public static event EvaluationEventHandler EvaluationBegin;

        /// <summary>
        ///     Emitted immediately after execution ends or fails
        /// </summary>
        [SupressImportIntoVM]
        public static event EvaluationEventHandler EvaluationEnd;

        /// <summary>
        /// Called immediately before evaluation starts
        /// </summary>
        /// <param name="engine">The engine used to do the evaluation</param>
        /// <param name="scope">The scope in which the code is executed</param>
        /// <param name="code">The code to be evaluated</param>
        /// <param name="bindingValues">The binding values - these are already added to the scope when called</param>
        private static void OnEvaluationBegin(  ScriptEngine engine, 
                                                ScriptScope scope, 
                                                string code, 
                                                IList bindingValues )
        {
            if (EvaluationBegin != null)
            {
                EvaluationBegin(EvaluationState.Begin, engine, scope, code, bindingValues);
            }
        }

        /// <summary>
        /// Called when the evaluation has completed successfully or failed
        /// </summary>
        /// <param name="isSuccessful">Whether the evaluation succeeded or not</param>
        /// <param name="engine">The engine used to do the evaluation</param>
        /// <param name="scope">The scope in which the code is executed</param>
        /// <param name="code">The code to that was evaluated</param>
        /// <param name="bindingValues">The binding values - these are already added to the scope when called</param>
        private static void OnEvaluationEnd( bool isSuccessful,
                                            ScriptEngine engine,
                                            ScriptScope scope,
                                            string code,
                                            IList bindingValues)
        {
            if (EvaluationEnd != null)
            {
                EvaluationEnd( isSuccessful ? EvaluationState.Success : EvaluationState.Failed, 
                    engine, scope, code, bindingValues);
            }
        }

        #endregion

        static void sWr_StringWritten(object sender, MyEvtArgs<string> e)
        {
            var fullpath = @"C:\Users\radug\Desktop\testing\debug.log";
            var message = " ------------------> Event was raised at : " + DateTime.Now.ToString() + Environment.NewLine;
            System.IO.File.AppendAllText(fullpath, message);
        }
    }

    // DEBUG EVENT ARGS
    public class MyEvtArgs<T> : EventArgs
    {
        public T Value
        {
            get;
            private set;
        }
        public MyEvtArgs(T value)
        {
            this.Value = value;
        }
    }

    // DEBUG EVENT
    public class EventRaisingStreamWriter : StreamWriter
    {
        // Event
        public event EventHandler<MyEvtArgs<string>> StringWritten;

        // CTOR
        public EventRaisingStreamWriter(Stream s) : base(s)
        { }

        // Private Methods
        private void LaunchEvent(string txtWritten)
        {
            if (StringWritten != null)
            {
                StringWritten(this, new MyEvtArgs<string>(txtWritten));
                var fullpath = @"C:\Users\radug\Desktop\testing\debugEvents.log";
                var message = "Event triggered at : " + DateTime.Now.ToString() + Environment.NewLine;
                System.IO.File.AppendAllText(fullpath, message);

            }
        }

        // Overrides
        public override void Write(string value)
        {
            base.Write(value);
            LaunchEvent(value);
        }
        public override void Write(bool value)
        {
            base.Write(value);
            LaunchEvent(value.ToString());
        }
        // here override all writing methods...
    }

}
