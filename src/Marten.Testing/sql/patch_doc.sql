
CREATE OR REPLACE FUNCTION public.mt_transform_patch_doc(doc JSONB, patch JSONB) RETURNS JSONB AS $$

  var module = {export: {}};

  'use strict';

function findDeep(doc, parts){
    var element = doc;

    for (var i = 0; i < parts.length; i++){
        var child = parts[i];
        if (!element.hasOwnProperty(child)){
            element[child] = {};
        }

        element = element[child];
    }

    return element;
}

function locate(doc, patch){
    var parts = patch.path.split('.');

    var element, prop;

    if (parts.length > 0){
        prop = parts.pop();
        element = findDeep(doc, parts);
    }
    else {
        prop = patch.path;
        element = doc;
    }

    return {element: element, prop: prop};
}

function setValue(doc, patch, location){
    location.element[location.prop] = patch.value;

    return doc;
}



function incrementValue(doc, patch, location){
    var interval = 1;
    if (patch.increment){
        interval = parseInt(patch.increment);
    }

    var existing = 0;
    if (location.element[location.prop]){
        existing = parseInt(location.element[location.prop]);
    }

    location.element[location.prop] = existing + interval;

    return doc;
}

function incrementFloat(doc, patch, location){
    var interval = 1;
    if (patch.increment){
        interval = parseFloat(patch.increment);
    }

    var existing = 0;
    if (location.element[location.prop]){
        existing = parseFloat(location.element[location.prop]);
    }

    location.element[location.prop] = existing + interval;

    return doc;
}

function appendElement(doc, patch, location){
    if (!location.element[location.prop]){
        location.element[location.prop] = [];
    }

    location.element[location.prop].push(patch.value);

    return doc;
}

function insertElement(doc, patch, location){
    if (!location.element[location.prop]){
        location.element[location.prop] = [];
    }

    var array = location.element[location.prop];

    var index = 0;
    if (patch.index){
        index = parseInt(patch.index);
    }

    array.splice(index, 0, patch.value);

    return doc;
}

function rename(doc, patch, location){
    var actual = location.element[location.prop];
    delete location.element[location.prop];

    location.element[patch.to] = actual;

    return doc;
}

function removeElement(doc, patch, location){
    var array = location.element[location.prop];
    if (!array) return doc;

    var indices = [];
    array.every(function(element, index, _array) {
      if (!deepEquals(patch.value, element)) return true;
      indices.push(index);
      return patch.action === 1;
    });

    for (var i = indices.length - 1; i > -1; i--) {
      array.splice(indices[i], 1);
    }

    return doc;
}

function deepEquals(x, y) {
    if (x === null || x === undefined || y === null || y === undefined) { return x === y; }
    if (x === y || x.valueOf() === y.valueOf()) { return true; }
    if (Array.isArray(x) && x.length !== y.length) { return false; }

    if (!(x instanceof Object)) { return false; }
    if (!(y instanceof Object)) { return false; }

    var p = Object.keys(x);
    return Object.keys(y).every(function (i) { return p.indexOf(i) !== -1; }) &&
        p.every(function (i) { return deepEquals(x[i], y[i]); });
}

var ops = {
    'set': setValue,
    'increment': incrementValue,
    'increment_float': incrementFloat,
    'append': appendElement,
    'rename': rename,
    'insert': insertElement,
    'remove': removeElement
}

module.exports = function(doc, patch){
    // TODO -- throw if not a handler
    var handler = ops[patch.type];

    var location = locate(doc, patch);

    return handler(doc, patch, location);
}


  var func = module.exports;

  return func(doc, patch);

$$ LANGUAGE plv8 IMMUTABLE STRICT;

