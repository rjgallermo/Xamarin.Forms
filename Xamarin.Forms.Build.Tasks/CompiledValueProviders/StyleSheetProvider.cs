﻿using System;
using System.Collections.Generic;
using System.Linq;

using System.Xml;

using Mono.Cecil;
using Mono.Cecil.Cil;

using Xamarin.Forms.Build.Tasks;
using Xamarin.Forms.Xaml;

using static Mono.Cecil.Cil.Instruction;
using static Mono.Cecil.Cil.OpCodes;


namespace Xamarin.Forms.Core.XamlC
{
	class StyleSheetProvider : ICompiledValueProvider
	{
		public IEnumerable<Instruction> ProvideValue(VariableDefinitionReference vardefref, ModuleDefinition module, BaseNode node, ILContext context)
		{
			INode sourceNode = null;
			((IElementNode)node).Properties.TryGetValue(new XmlName("", "Source"), out sourceNode);
			if (sourceNode == null)
				((IElementNode)node).Properties.TryGetValue(new XmlName(XamlParser.XFUri, "Source"), out sourceNode);

			INode styleNode = null;
			if (!((IElementNode)node).Properties.TryGetValue(new XmlName("", "Style"), out styleNode) &&
				!((IElementNode)node).Properties.TryGetValue(new XmlName(XamlParser.XFUri, "Style"), out styleNode) &&
				((IElementNode)node).CollectionItems.Count == 1)
				styleNode = ((IElementNode)node).CollectionItems[0];

			if (sourceNode != null && styleNode != null)
				throw new XamlParseException($"StyleSheet can not have both a Source and a content", node);

			if (sourceNode == null && styleNode == null)
				throw new XamlParseException($"StyleSheet require either a Source or a content", node);

			if (styleNode != null && !(styleNode is ValueNode))
				throw new XamlParseException($"Style property or Content is not a string literal", node);

			if (sourceNode != null && !(sourceNode is ValueNode))
				throw new XamlParseException($"Source property is not a string literal", node);

			if (styleNode != null) {
				var style = (styleNode as ValueNode).Value as string;
				yield return Create(Ldstr, style);

				var fromString = module.ImportReferenceCached(typeof(StyleSheets.StyleSheet).GetMethods().FirstOrDefault(mi => mi.Name == nameof(StyleSheets.StyleSheet.FromString) && mi.GetParameters().Length == 1));
				yield return Create(Call, module.ImportReference(fromString));
			}
			else {
				string source = (sourceNode as ValueNode)?.Value as string;
				INode rootNode = node;
				while (!(rootNode is ILRootNode))
					rootNode = rootNode.Parent;

				var rootTargetPath = RDSourceTypeConverter.GetPathForType(module, ((ILRootNode)rootNode).TypeReference);
				var uri = new Uri(source, UriKind.Relative);

				var resourcePath = ResourceDictionary.RDSourceTypeConverter.GetResourcePath(uri, rootTargetPath);
				//fail early
				var resourceId = XamlCTask.GetResourceIdForPath(module, resourcePath);
				if (resourceId == null)
					throw new XamlParseException($"Resource '{source}' not found.", node);

				var getTypeFromHandle = module.ImportReferenceCached(typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), new[] { typeof(RuntimeTypeHandle) }));
				var getAssembly = module.ImportReferenceCached(typeof(Type).GetProperty(nameof(Type.Assembly)).GetGetMethod());
				yield return Create(Ldtoken, module.ImportReference(((ILRootNode)rootNode).TypeReference));
				yield return Create(Call, module.ImportReference(getTypeFromHandle));
				yield return Create(Callvirt, module.ImportReference(getAssembly)); //assembly

				yield return Create(Ldstr, resourceId); //resourceId

				foreach (var instruction in node.PushXmlLineInfo(context))
					yield return instruction; //lineinfo

				var fromAssemblyResource = module.ImportReferenceCached(typeof(StyleSheets.StyleSheet).GetMethods().FirstOrDefault(mi => mi.Name == nameof(StyleSheets.StyleSheet.FromAssemblyResource) && mi.GetParameters().Length == 3));
				yield return Create(Call, module.ImportReference(fromAssemblyResource));
			}

			//the variable is of type `object`. fix that
			var vardef = new VariableDefinition(module.ImportReferenceCached(typeof(StyleSheets.StyleSheet)));
			yield return Create(Stloc, vardef);
			vardefref.VariableDefinition = vardef;
		}
	}
}