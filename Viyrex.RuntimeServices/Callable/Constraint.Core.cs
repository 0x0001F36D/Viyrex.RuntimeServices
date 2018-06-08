﻿// Author: Viyrex(aka Yuyu)
// Contact: mailto:viyrex.aka.yuyu@gmail.com
// Github: https://github.com/0x0001F36D

#define NOT_SUPPORT_GENERIC_TYPE

namespace Viyrex.RuntimeServices.Callable
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Runtime.CompilerServices;
    using Exceptions;
    using Internal;
    using static Internal.SupportUtil;

    /// <summary>
    /// 提供製作動態 <typeparamref name="TConstraint"/> 物件的支援， 且 <typeparamref name="TConstraint"/>
    /// 必須是無泛型參數的 <see langword="abstract"/><see langword="class"/>、 <see langword="class"/> 或 <see
    /// langword="interface"/> 類型
    /// </summary>
    /// <typeparam name="TConstraint">
    /// 欲製作實體之類型。 必須是無泛型參數的 <see langword="abstract"/><see langword="class"/>、 <see
    /// langword="class"/> 或 <see langword="interface"/> 類型
    /// </typeparam>
    /// <exception cref="GenericArgumentException{T}"/>
    public sealed partial class Constraint<TConstraint>
    {
        #region Structs

        private struct Bag : IInternalBag
        {
            #region Constructors

            public Bag(ConstructorInfo ctor)
                : this(ctor.DeclaringType, ctor.GetParameters().Select(v => v.ParameterType).ToArray())
            {
            }

            public Bag(System.Type @return, System.Type[] arguments)
            {
                this.Return = @return;
                this.Arguments = arguments;
                this._hash = GetHashCode(@return, arguments);
            }

            #endregion Constructors

            #region Fields

            private readonly int _hash;

            #endregion Fields

            #region Properties

            public System.Type[] Arguments { get; }

            public Type Return { get; }

            #endregion Properties

            #region Methods

            public static int GetHashCode(System.Type @return, System.Type[] arguments)
            {
                var s = @return.GetHashCode() * -2;
                for (int i = 0; i < arguments.Length; i++)
                {
                    var curr = arguments[i].GetHashCode() << i;
                    s += curr;
                }
                return s;
            }

            public static bool operator !=(Bag l, Bag r)
            {
                return l._hash != r._hash;
            }

            public static bool operator !=(Bag bag, int i)
            {
                return bag._hash != i;
            }

            public static bool operator ==(Bag l, Bag r)
            {
                return l._hash == r._hash;
            }

            public static bool operator ==(Bag bag, int i)
            {
                return bag._hash == i;
            }

            public override bool Equals(object obj)
            {
                if (obj is Bag bag)
                {
                    return bag == this;
                }

                return false;
            }

            public override int GetHashCode() => this._hash;

            #endregion Methods
        }

        #endregion Structs

        #region Constructors

        static Constraint()
        {
            s_GenericType = typeof(TConstraint);
            s_HowToTreat = IsSupported(s_GenericType);

            if (s_HowToTreat == TreatmentMode.NotTreated)
                throw new GenericArgumentException<TConstraint>();

            SupportedGenericType();
        }

        private Constraint()
        {
            this._multicastDelegete = typeof(MulticastDelegate);
            this._object = typeof(object);
            this._intPtr = typeof(IntPtr);

            var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("#"), AssemblyBuilderAccess.RunAndCollect);
            this._module = assembly.DefineDynamicModule(Guid.NewGuid().ToString());

            this.InternalCaches = new Dictionary<IInternalBag, Delegate>();

            // get all types and add into typeCache
            var predicator = default(Func<System.Type, bool>);
            switch (s_HowToTreat)
            {
                case TreatmentMode.Interface:
                    predicator = (type) => type.GetInterfaces().Any(v => v == this.ConstraintType);
                    break;

                case TreatmentMode.AbstractClass:
                case TreatmentMode.Class:
                    predicator = (type) => type.IsSubclassOf(this.ConstraintType);
                    break;

                case TreatmentMode.NotTreated:
                default:
                    break;
            }

            var types = Callable.Types.List.Where(predicator);
            this.Types = types.ToArray();

            var ctors = types.SelectMany(c => c.GetConstructors(ALL));
            foreach (var ctor in ctors)
            {
                if (this.TryBuildWith(ctor, out var dele))
                    this.InternalCaches.Add(new Bag(ctor), dele);
            }
        }

        #endregion Constructors

        #region Fields

        private const BindingFlags ALL = (BindingFlags)17301375;

        private const string INVOKE = "Invoke";

        private const MethodAttributes PUBLIC_HIDEBYSIG = MethodAttributes.HideBySig | MethodAttributes.Public;

        private const TypeAttributes PUBLIC_SEALED = TypeAttributes.Sealed | TypeAttributes.Public;

        private static readonly System.Type s_GenericType;

        private static readonly TreatmentMode s_HowToTreat;

        private static readonly object s_locker = new object();

        private static volatile Constraint<TConstraint> s_instance;
        private readonly ModuleBuilder _module;

        private readonly System.Type _multicastDelegete, _object, _intPtr;

        #endregion Fields

        #region Properties

        /// <summary>
        /// 提供一組 Collector 實體做為 <typeparamref name="TConstraint"/> 類型/介面的集合器
        /// </summary>
        public static Constraint<TConstraint> Collector
        {
            get
            {
                if (s_instance == null)
                    lock (s_locker)
                        if (s_instance == null)
                            s_instance = new Constraint<TConstraint>();
                return s_instance;
            }
        }

        /// <summary>
        /// 取得 <typeparamref name="TConstraint"/> 類型的 <see cref="System.Type"/> 物件
        /// </summary>
        public Type ConstraintType => s_GenericType;

        /// <summary>
        /// 取得所有實作或繼承 <typeparamref name="TConstraint"/> 類型的 <see cref="System.Type"/> 物件
        /// </summary>
        public System.Type[] Types { get; }

        internal Dictionary<IInternalBag, Delegate> InternalCaches { get; }

        internal IInternalBag CreateNewBag(System.Type @return, System.Type[] arguments)
        {
            return new Bag(@return, arguments);
        }

        #endregion Properties

        #region Methods

        /// <summary>
        /// 製作 <see langword="delegate"/> 類型的動態類型
        /// </summary>
        /// <param name="delegateParameters">欲製作之動態 <see langword="delegate"/> 類型的參數</param>
        /// <param name="delegateReturnType">欲製作之動態 <see langword="delegate"/> 類型的回傳類型</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Type PlainMake(ParameterInfo[] delegateParameters, System.Type delegateReturnType)
        {
            var delegateTypeName = $"#{Guid.NewGuid()}";

            var parameterTypes = delegateParameters.ToTypeArray();

            // class
            var typeTemplate = this._module.DefineType(delegateTypeName, PUBLIC_SEALED, this._multicastDelegete);

            // .ctor(object, IntPtr)
            var ctor = typeTemplate.DefineConstructor(MethodAttributes.RTSpecialName | PUBLIC_HIDEBYSIG, CallingConventions.Standard, new[] { this._object, this._intPtr });
            ctor.SetImplementationFlags(MethodImplAttributes.CodeTypeMask);

            // returnType Invoke(parameters[0], parameters[1], ...)
            var invoke = typeTemplate.DefineMethod(INVOKE, MethodAttributes.Virtual | PUBLIC_HIDEBYSIG, delegateReturnType, parameterTypes);
            invoke.SetImplementationFlags(MethodImplAttributes.CodeTypeMask);

            // skip i = 0 cuz 0 equals then "this"(aka MSIL.Ldarg0)
            for (int i = delegateParameters.Length; i > 0;)
                invoke.DefineParameter(i--, ParameterAttributes.None, delegateParameters[i].Name);

            // create delegate type 動態委派類型
            var delegateType = typeTemplate.CreateType();

            return delegateType;
        }

        [Conditional("NOT_SUPPORT_GENERIC_TYPE")]
        private static void SupportedGenericType()
        {
            if (s_GenericType.IsGenericType)
                throw new GenericArgumentException<TConstraint>("Can't process generic type");
        }

        #endregion Methods
    }
}