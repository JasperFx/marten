var Patch = require('./../javascript/mt_patching');
var expect = require('chai').expect;

describe('Patching API', function() {
  it('can set the first level prop', function() {
    var doc = {name: 'Frodo'};
    
    var patch = {type: 'set', path: 'name', value: 'Bilbo'};
    
    expect(Patch(doc, patch)).to.deep.equal({name: 'Bilbo'}); 
  });
  
  it('can set a deep prop that already exists', function() {
     var doc = {member: {name: 'Frodo'}}; 
     var patch = {type: 'set', path: 'member.name', value: 'Bilbo'};
     
     expect(Patch(doc, patch)).to.deep.equal({member: {name: 'Bilbo'}}); 
  });
  
  it('can set a deep prop does not already exists', function() {
     var doc = {}; 
     var patch = {type: 'set', path: 'member.name', value: 'Bilbo'};
     
     expect(Patch(doc, patch)).to.deep.equal({member: {name: 'Bilbo'}}); 
  });
  
  it('can set a 3 deep prop that already exists', function() {
     var doc = {quest: {member: {name: 'Frodo'}}}; 
     var patch = {type: 'set', path: 'quest.member.name', value: 'Bilbo'};
     
     expect(Patch(doc, patch)).to.deep.equal({quest: {member: {name: 'Bilbo'}}}); 
  });
  
  it('can set a 3 deep prop that does not yet exists', function() {
     var doc = {}; 
     var patch = {type: 'set', path: 'quest.member.name', value: 'Bilbo'};
     
     expect(Patch(doc, patch)).to.deep.equal({quest: {member: {name: 'Bilbo'}}}); 
  });
  
  it('can increment int first level', function() {
    var doc = {count: 5};
    var patch = {type: 'increment', path: 'count', increment: 1};
    
    expect(Patch(doc, patch)).to.deep.equal({count: 6});
  });
  
  it('can increment int first level, non default increment', function() {
    var doc = {count: 5};
    var patch = {type: 'increment', path: 'count', increment: 3};
    
    expect(Patch(doc, patch)).to.deep.equal({count: 8});
  });
  
  it('can increment int first level default increment', function() {
    var doc = {count: 5};
    var patch = {type: 'increment', path: 'count'};
    
    expect(Patch(doc, patch)).to.deep.equal({count: 6});
  });
  
  it('can increment int 2 deep', function() {
    var doc = {summary: {count: 5}};
    var patch = {type: 'increment', path: 'summary.count'};
    
    expect(Patch(doc, patch)).to.deep.equal({summary: {count: 6}});
  });
  
  it('can increment int 3 deep', function() {
    var doc = {region: {summary: {count: 5}}};
    var patch = {type: 'increment', path: 'region.summary.count'};
    
    expect(Patch(doc, patch)).to.deep.equal({region: {summary: {count: 6}}});
  });
  
  it('can increment float first level', function() {
    var doc = {count: 5.1};
    var patch = {type: 'increment_float', path: 'count', increment: 1};
    
    expect(Patch(doc, patch)).to.deep.equal({count: 6.1});
  });
  
  it('can increment float first level, non default increment', function() {
    var doc = {count: 5.1};
    var patch = {type: 'increment_float', path: 'count', increment: 3.2};
    
    expect(Patch(doc, patch)).to.deep.equal({count: 8.3});
  });
  
  it('can increment float first level default increment', function() {
    var doc = {count: 5.1};
    var patch = {type: 'increment_float', path: 'count'};
    
    expect(Patch(doc, patch)).to.deep.equal({count: 6.1});
  });
  
  it('can increment float 2 deep', function() {
    var doc = {summary: {count: 5.1}};
    var patch = {type: 'increment_float', path: 'summary.count'};
    
    expect(Patch(doc, patch)).to.deep.equal({summary: {count: 6.1}});
  });
  
  it('can increment float 3 deep', function() {
    var doc = {region: {summary: {count: 5.3}}};
    var patch = {type: 'increment_float', path: 'region.summary.count', increment: 1.1};
    
    expect(Patch(doc, patch)).to.deep.equal({region: {summary: {count: 6.4}}});
  });
  
  it('can append to a first level array', function() {
    var doc = {numbers: []};
    
    var patch = {type: 'append', path: 'numbers', value: 5};
    
    expect(Patch(doc, patch)).to.deep.equal({numbers: [5]});
  });
  
  it('can append to a first level array with existing elements', function() {
    var doc = {numbers: [3, 4]};
    
    var patch = {type: 'append', path: 'numbers', value: 5};
    
    expect(Patch(doc, patch)).to.deep.equal({numbers: [3, 4, 5]});
  });
  
  it('can append to a first level array when the array does not already exist', function() {
    var doc = {};
    
    var patch = {type: 'append', path: 'numbers', value: 5};
    
    expect(Patch(doc, patch)).to.deep.equal({numbers: [5]});
  });
  
  it('can append to a 2nd level array when the array does not already exist', function() {
    var doc = {};
    
    var patch = {type: 'append', path: 'region.numbers', value: 5};
    
    expect(Patch(doc, patch)).to.deep.equal({region: {numbers: [5]}});
  });
  
  it('can append to a 3rd level array when the array does not already exist', function() {
    var doc = {division: {region: {numbers: [3, 4]}}};
    
    var patch = {type: 'append', path: 'division.region.numbers', value: 5};
    
    expect(Patch(doc, patch)).to.deep.equal({division: {region: {numbers: [3, 4, 5]}}});
  });
  
  it('can rename a prop shallow', function() {
    var doc = {number: 1};
    
    var patch = {type: 'rename', path: 'number', to: 'value'};
    
    expect(Patch(doc, patch)).to.deep.equal({value: 1});
  });
  
  it('can rename a prop 2 deep', function() {
    var doc = {region: {number: 1}};
    
    var patch = {type: 'rename', path: 'region.number', to: 'value'};
    
    expect(Patch(doc, patch)).to.deep.equal({region: {value: 1}});
  });
  
  it('can rename a prop 3 deep', function() {
    var doc = {country: {region: {number: 1}}};
    
    var patch = {type: 'rename', path: 'country.region.number', to: 'value'};
    
    expect(Patch(doc, patch)).to.deep.equal({country: {region: {value: 1}}});
  });
  
  it('can insert into a child collection with default', function() {
    var doc = {numbers: [3, 4]};
    
    var patch = {type: 'insert', path: 'numbers', value: 5};
    
    expect(Patch(doc, patch)).to.deep.equal({numbers: [5, 3, 4]});
  });
  
  it('can insert into a child collection with designated index', function() {
    var doc = {numbers: [3, 4]};
    
    var patch = {type: 'insert', path: 'numbers', value: 5, index: 1};
    
    expect(Patch(doc, patch)).to.deep.equal({numbers: [3, 5, 4]});
  });
  
  it('can insert into a 2 deep child collection with designated index', function() {
    var doc = {region: {numbers: [3, 4]}};
    
    var patch = {type: 'insert', path: 'region.numbers', value: 5, index: 1};
    
    expect(Patch(doc, patch)).to.deep.equal({region: {numbers: [3, 5, 4]}});
  });
});