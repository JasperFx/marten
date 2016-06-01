function setValue(doc, patch){
    var parts = patch.path.split('.');
    
    var element = doc;
    if (parts.length > 0){
        var lastPart = parts.pop();
        
        for (var i = 0; i < parts.length; i++){
            var child = parts[i];
            if (!element.hasOwnProperty(child)){
                element[child] = {};
            }
            
            element = element[child];
        }
        
        element[lastPart] = patch.value;
    }
    else {
        doc[patch.path] = patch.value;
    }
    
    return doc;
}

var ops = {
    'set': setValue
    
}

module.exports = function(doc, patch){
    // TODO -- throw if not a handler
    var handler = ops[patch.type];
    
    return handler(doc, patch);    
}