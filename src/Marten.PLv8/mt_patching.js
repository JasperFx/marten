'use strict';

function findDeep(doc, parts){
    var element = doc;

    for (var i = 0; i < parts.length; i++){
        var child = parts[i];
        if (!element.hasOwnProperty(child) || !element[child]){
            element[child] = {};
        }

        element = element[child];
    }

    return element;
}

function locate(doc, path){
    var parts = path.split('.');

    var element, prop;

    if (parts.length > 0){
        prop = parts.pop();
        element = findDeep(doc, parts);
    }
    else {
        prop = path;
        element = doc;
    }

    return {element: element, prop: prop};
}

function setValue(doc, patch, location){
    location.element[location.prop] = patch.value;

    return doc;
}

function deleteValue(doc, patch, location){
    delete location.element[location.prop];

    return doc;
}

function duplicateValue(doc, patch, location){
    var value = location.element[location.prop];
    for (var i = 0; i < patch.targets.length; i++) {
      var copyTo = locate(doc, patch.targets[i]);
      copyTo.element[copyTo.prop] = value;
    }

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

function appendElementIfNotExists(doc, patch, location){
     if (!location.element[location.prop]){
        location.element[location.prop] = [];
    }

    var array = location.element[location.prop];
    var patchValueAsJSONString = JSON.stringify(patch.value);
    for(var i = 0; i < array.length; i++)
    {
        var elementAsJSONString = JSON.stringify(array[i])
        if (patchValueAsJSONString === elementAsJSONString)
            return doc;
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

function insertElementIfNotExists(doc, patch, location){
    if (!location.element[location.prop]){
        location.element[location.prop] = [];
    }

    var array = location.element[location.prop];

    var index = 0;
    if (patch.index){
        index = parseInt(patch.index);
    }

    var patchValueAsJSONString = JSON.stringify(patch.value);
    var elementAsJSONString = JSON.stringify(array[index]);
    if (elementAsJSONString !== patchValueAsJSONString){
        array.splice(index, 0, patch.value);
    }

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
    'delete': deleteValue,
    'duplicate': duplicateValue,
    'increment': incrementValue,
    'increment_float': incrementFloat,
    'append': appendElement,
    'append_if_not_exists': appendElementIfNotExists,
    'rename': rename,
    'insert': insertElement,
    'insert_if_not_exists' : insertElementIfNotExists,
    'remove': removeElement
}

module.exports = function(doc, patch){
    // TODO -- throw if not a handler
    var handler = ops[patch.type];

    var location = locate(doc, patch.path);

    return handler(doc, patch, location);
}
