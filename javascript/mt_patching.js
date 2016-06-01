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

function rename(doc, patch, location){
    var actual = location.element[location.prop];
    delete location.element[location.prop];
    
    location.element[patch.to] = actual;
    
    return doc;
}

var ops = {
    'set': setValue,
    'increment': incrementValue,
    'increment_float': incrementFloat,
    'append': appendElement,
    'rename': rename
    
}

module.exports = function(doc, patch){
    // TODO -- throw if not a handler
    var handler = ops[patch.type];
    
    var location = locate(doc, patch);
    
    return handler(doc, patch, location);    
}