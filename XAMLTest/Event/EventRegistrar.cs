﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace XamlTest.Event
{
    internal static class EventRegistrar
    {
        private class EventDetails
        {
            public List<object[]> Invocations { get; } = new();
            public Delegate Delegate { get; }
            public EventInfo Event { get; }
            public object? Source { get; }

            public EventDetails(EventInfo eventInfo, Delegate @delegate, object? source)
            {
                Event = eventInfo;
                Delegate = @delegate;
                Source = source;
            }
        }

        private static object SyncObject { get; } = new();

        private static Dictionary<string, EventDetails> RegisteredEvents { get; } = new();

        private static Dictionary<string, List<object[]>> EventInvocations { get; } = new();
        private static Dictionary<string, Delegate> EventDelegates { get; } = new();

        public static void AddInvocation(string eventId, object[] parameters)
        {
            lock(SyncObject)
            {
                if (RegisteredEvents.TryGetValue(eventId, out EventDetails? eventDetails))
                {
                    eventDetails.Invocations.Add(parameters);
                }
            }
        }

        public static bool Unregister(string eventId)
        {
            lock (SyncObject)
            {
                if (RegisteredEvents.TryGetValue(eventId, out EventDetails? eventDetails))
                {
                    MethodInfo? removeMethod = eventDetails.Event.GetRemoveMethod();
                    removeMethod?.Invoke(eventDetails.Source, new object[] { eventDetails.Delegate });
                    return removeMethod != null;
                }
            }
            return false;
        }

        internal static IReadOnlyList<object[]>? GetInvocations(string eventId)
        {
            lock (SyncObject)
            {
                if (RegisteredEvents.TryGetValue(eventId, out EventDetails? eventDetails))
                {
                    return eventDetails.Invocations;
                }
            }
            return null;
        }

        public static void Regsiter(string eventId, EventInfo eventInfo, object? source)
        {
            if (eventId is null)
            {
                throw new ArgumentNullException(nameof(eventId));
            }

            if (eventInfo is null)
            {
                throw new ArgumentNullException(nameof(eventInfo));
            }

            Type tDelegate = eventInfo.EventHandlerType;
            
            Type returnType = GetDelegateReturnType(tDelegate);
            if (returnType != typeof(void))
                throw new Exception("Delegate has a return type.");

            var delegateParameterTypes = GetDelegateParameterTypes(tDelegate);

            DynamicMethod handler =
                new DynamicMethod("",
                                  null,
                                  delegateParameterTypes,
                                  typeof(EventRegistrar));

            // Generate a method body. This method loads a string, calls
            // the Show method overload that takes a string, pops the
            // return value off the stack (because the handler has no
            // return type), and returns.
            //
            ILGenerator ilgen = handler.GetILGenerator();
            var method = typeof(EventRegistrar)
                .GetMethod(nameof(EventRegistrar.AddInvocation));
            int foo = 0;
            string bar = "";
            object[] array = new object[] { foo, bar };

            ilgen.Emit(OpCodes.Ldstr, eventId);
            ilgen.Emit(OpCodes.Ldc_I4, delegateParameterTypes.Length);
            ilgen.Emit(OpCodes.Newarr, typeof(object));
            
            for(int i = 0; i < delegateParameterTypes.Length; i++)
            {
                ilgen.Emit(OpCodes.Dup);
                ilgen.Emit(OpCodes.Ldc_I4, i);
                ilgen.Emit(OpCodes.Ldarg, i);
                if (!delegateParameterTypes[i].IsClass)
                {
                    ilgen.Emit(OpCodes.Box, delegateParameterTypes[i]);
                }
                ilgen.Emit(OpCodes.Stelem_Ref);
            }
            ilgen.Emit(OpCodes.Call, method);
            ilgen.Emit(OpCodes.Ret);

            // Complete the dynamic method by calling its CreateDelegate
            // method. Use the "add" accessor to add the delegate to
            // the invocation list for the event.
            //
            MethodInfo addHandler = eventInfo.GetAddMethod();
            Delegate dEmitted = handler.CreateDelegate(tDelegate);
            addHandler.Invoke(source, new object[] { dEmitted });

            lock(RegisteredEvents)
            {
                RegisteredEvents.Add(eventId, new EventDetails(eventInfo, dEmitted, source));
            }
        }

        private static Type[] GetDelegateParameterTypes(Type d)
        {
            if (d.BaseType != typeof(MulticastDelegate))
                throw new Exception("Not a delegate.");

            MethodInfo invoke = d.GetMethod("Invoke");
            if (invoke is null)
                throw new Exception("Not a delegate.");

            ParameterInfo[] parameters = invoke.GetParameters();
            Type[] typeParameters = new Type[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                typeParameters[i] = parameters[i].ParameterType;
            }
            return typeParameters;
        }

        private static Type GetDelegateReturnType(Type d)
        {
            if (d.BaseType != typeof(MulticastDelegate))
                throw new Exception("Not a delegate.");

            MethodInfo? invoke = d.GetMethod(nameof(Action.Invoke));
            if (invoke is null)
                throw new Exception($"Could not find {nameof(Action.Invoke)} method on delegate {d.FullName}");

            return invoke.ReturnType;
        }

    }
}
