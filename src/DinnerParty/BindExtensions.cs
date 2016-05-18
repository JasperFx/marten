using System;
using Nancy;
using Nancy.ModelBinding;
using System.ComponentModel;

namespace DinnerParty
{
    public static class BindExtensions
    {
        /// <summary>
        /// BindTo extension method to bind to same instance. Pull Request sent to NancyFx with this extension so it might make it into Nancy 0.12
        /// </summary>
        /// <typeparam name="TModel"></typeparam>
        /// <param name="module"></param>
        /// <param name="instance"></param>
        /// <param name="blacklistedProperties"></param>
        public static void BindTo<TModel>(this NancyModule module, TModel instance, params string[] blacklistedProperties)
        {
            if (instance == null)
                throw new ArgumentNullException("Bind instance is null");

            TModel boundModel = module.Bind(blacklistedProperties);

            foreach (PropertyDescriptor item in TypeDescriptor.GetProperties(boundModel))
            {
                var value = item.GetValue(boundModel);
                if (value != null)
                    item.SetValue(instance, value);
            }

        }
    }
}