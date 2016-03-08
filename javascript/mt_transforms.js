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
	transforms: {},

	snapshot: function(def) {
		var snapshot = new Snapshot(def);
		this.transforms[snapshot.name] = snapshot;

		return snapshot;
	},

	transform: function(def){
		var transformer = new Transform(def);
		this.transforms[transformer.name] = transformer;

		return transformer;
	},

	usages: function(){
		var list = [];

		for (var key in this.transforms){
			list = list.concat(this.transforms[key].usages());
		}

		return list;
	},

	// TODO: might add version here
	apply_transformation: function(projection, event_type, event, stream_id, event_id){
		if (this.transforms.hasOwnProperty(projection)){
			return this.transforms[projection].transform(event_type, event, {id: event_id, stream: stream_id});
		}

		throw new Error('Unknown Projection: ' + projection);
	},

	apply_aggregation: function(projection, event_type, event, aggregate, stream_id, event_id){
		if (this.transforms.hasOwnProperty(projection)){
			return this.transforms[projection].transform(event_type, event, aggregate, {id: event_id, stream: stream_id}) || aggregate;
		}

		throw new Error('Unknown Projection: ' + projection);
	}





}