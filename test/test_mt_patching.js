var Patch = require('./../javascript/mt_patching');
var expect = require('chai').expect;

describe('Patching API', () => {
  it('can set the first level prop', () => {
    var doc = {name: 'Frodo'};
    
    var patch = {type: 'set', path: 'name', value: 'Bilbo'};
    
    expect(Patch(doc, patch)).to.deep.equal({name: 'Bilbo'}); 
  });
  
  it('can set a deep prop that already exists', () => {
     var doc = {member: {name: 'Frodo'}}; 
     var patch = {type: 'set', path: 'member.name', value: 'Bilbo'};
     
     expect(Patch(doc, patch)).to.deep.equal({member: {name: 'Bilbo'}}); 
  });
  
  it('can set a deep prop does not already exists', () => {
     var doc = {}; 
     var patch = {type: 'set', path: 'member.name', value: 'Bilbo'};
     
     expect(Patch(doc, patch)).to.deep.equal({member: {name: 'Bilbo'}}); 
  });
  
  it('can set a 3 deep prop that already exists', () => {
     var doc = {quest: {member: {name: 'Frodo'}}}; 
     var patch = {type: 'set', path: 'quest.member.name', value: 'Bilbo'};
     
     expect(Patch(doc, patch)).to.deep.equal({quest: {member: {name: 'Bilbo'}}}); 
  });
  
  it('can set a 3 deep prop that does not yet exists', () => {
     var doc = {}; 
     var patch = {type: 'set', path: 'quest.member.name', value: 'Bilbo'};
     
     expect(Patch(doc, patch)).to.deep.equal({quest: {member: {name: 'Bilbo'}}}); 
  });
  
  it('can increment int first level', () => {
    var doc = {count: 5};
    var patch = {type: 'increment', path: 'count', increment: 1};
    
    expect(Patch(doc, patch)).to.deep.equal({count: 6});
  });
  
  it('can increment int first level, non default increment', () => {
    var doc = {count: 5};
    var patch = {type: 'increment', path: 'count', increment: 3};
    
    expect(Patch(doc, patch)).to.deep.equal({count: 8});
  });
  
  it('can increment int first level default increment', () => {
    var doc = {count: 5};
    var patch = {type: 'increment', path: 'count'};
    
    expect(Patch(doc, patch)).to.deep.equal({count: 6});
  });
  
  it('can increment int 2 deep', () => {
    var doc = {summary: {count: 5}};
    var patch = {type: 'increment', path: 'summary.count'};
    
    expect(Patch(doc, patch)).to.deep.equal({summary: {count: 6}});
  });
  
  it('can increment int 3 deep', () => {
    var doc = {region: {summary: {count: 5}}};
    var patch = {type: 'increment', path: 'region.summary.count'};
    
    expect(Patch(doc, patch)).to.deep.equal({region: {summary: {count: 6}}});
  });
  
  it('can increment float first level', () => {
    var doc = {count: 5.1};
    var patch = {type: 'increment_float', path: 'count', increment: 1};
    
    expect(Patch(doc, patch)).to.deep.equal({count: 6.1});
  });
  
  it('can increment float first level, non default increment', () => {
    var doc = {count: 5.1};
    var patch = {type: 'increment_float', path: 'count', increment: 3.2};
    
    expect(Patch(doc, patch)).to.deep.equal({count: 8.3});
  });
  
  it('can increment float first level default increment', () => {
    var doc = {count: 5.1};
    var patch = {type: 'increment_float', path: 'count'};
    
    expect(Patch(doc, patch)).to.deep.equal({count: 6.1});
  });
  
  it('can increment float 2 deep', () => {
    var doc = {summary: {count: 5.1}};
    var patch = {type: 'increment_float', path: 'summary.count'};
    
    expect(Patch(doc, patch)).to.deep.equal({summary: {count: 6.1}});
  });
  
  it('can increment float 3 deep', () => {
    var doc = {region: {summary: {count: 5.3}}};
    var patch = {type: 'increment_float', path: 'region.summary.count', increment: 1.1};
    
    expect(Patch(doc, patch)).to.deep.equal({region: {summary: {count: 6.4}}});
  });
});