function Snapshot(def){
	var self = this;

	self.name = def.name;
	self.type = 'snapshot';

	self.init = def.$init || function(){
		return {};
	}

	self.timing = def.timing || 'inline';

	self.usages = function(){
		var result = [];

		for (var key in def){
			if (key != '$init' && Function.prototype.isPrototypeOf(def[key])){
				var usage = {
					name: self.name,
					type: self.type,
					event_name: key,
					timing: self.timing
				}

				result.push(usage);
			}


			
		}

		return result;
	}

	self.transform = function(event_type, event, aggregate, metadata){
		aggregate = aggregate || self.init();

		result = aggregate;
		if (def.hasOwnProperty(event_type)){
			result = def[event_type](aggregate, event, metadata);
		}

		return result || aggregate;
	}
}

function Transform(def){
	var self = this;

	self.name = def.name;
	self.type = 'transform';

	self.timing = def.timing || 'inline';

	self.transform = function(event_type, event, metadata){
		var func = def[event_type];
		return func(event, metadata);
	}

	self.usages = function(){
		var result = [];

		for (var key in def){
			if (Function.prototype.isPrototypeOf(def[key])){
				var usage = {
					name: self.name,
					type: self.type,
					event_name: key,
					timing: self.timing
				}

				result.push(usage);
			}


			
		}

		return result;
	}
}

module.exports = {
	snapshot: function(def) {
		return new Snapshot(def);
	},

	transform: function(def){
		return new Transform(def);
	}
}